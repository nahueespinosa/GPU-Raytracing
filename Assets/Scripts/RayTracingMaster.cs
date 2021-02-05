using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using UnityEngine;

public class RayTracingMaster : MonoBehaviour
{
  public ComputeShader RayTracingShader;

  public Texture SkyboxTexture;

  public Light DirectionalLight;

  public Vector2 SphereRadius = new Vector2(3.0f, 8.0f);

  public uint SpheresMax = 100;

  public float SpherePlacementRadius = 100.0f;

  public int SphereSeed;

  private ComputeBuffer sphereBuffer;

  private RenderTexture target;

  private RenderTexture converged;

  private new Camera camera;

  private uint currentSample = 0;

  private Material addMaterial;

  private static bool meshObjectsNeedRebuilding = false;
  private static List<RayTracingObject> rayTracingObjects = new List<RayTracingObject>();

  public struct Sphere {
    public Vector3 position;
    public float radius;
    public Vector3 albedo;
    public Vector3 specular;
    public float smoothness;
    public Vector3 emission;
  };

  public struct MeshObject {
    public Matrix4x4 localToWorldMatrix;
    public int indices_offset;
    public int indices_count;
  };

  private static List<MeshObject> meshObjects = new List<MeshObject>();
  private static List<Vector3> vertices = new List<Vector3>();
  private static List<int> indices = new List<int>();
  private ComputeBuffer meshObjectBuffer;
  private ComputeBuffer vertexBuffer;
  private ComputeBuffer indexBuffer;

  private void OnEnable() {
    currentSample = 0;
    SetUpScene();
  }

  private void OnDisable() {
    if (sphereBuffer != null)
      sphereBuffer.Release();

    if (meshObjectBuffer != null)
      meshObjectBuffer.Release();

    if (vertexBuffer != null)
      vertexBuffer.Release();

    if (indexBuffer != null)
      indexBuffer.Release();
  }

  private void SetUpScene() {
    List<Sphere> spheres = new List<Sphere>();

    Random.InitState(SphereSeed);

    // Add a number of random spheres
    for (int i = 0; i < SpheresMax; i++) {
      Sphere sphere = new Sphere();

      // Radius and radius
      sphere.radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);
      Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
      sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);

      // Reject spheres that are intersecting others
      bool skip = false;

      foreach (Sphere other in spheres) {
        float minDist = sphere.radius + other.radius;
        if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist) {
          skip = true;
          break;
        }
      }

      if (!skip) {
        // Albedo and specular color
        Color color = Random.ColorHSV();

        bool metal = false;
        bool orb = false;

        float random = Random.value;

        if (random < 0.5f) {
          metal = true;
        } else if (random < 0.6f) {
          orb = true;
        }

        sphere.albedo = metal ? Vector3.zero : new Vector3(color.r, color.g, color.b);
        sphere.specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.04f;
        sphere.emission = orb ? Vector3.one : Vector3.zero;
        sphere.smoothness = 1.0f;

        // Add the sphere to the list
        spheres.Add(sphere);
      }
    }

    // Assign to compute buffer
    sphereBuffer = new ComputeBuffer(spheres.Count, Marshal.SizeOf<Sphere>());
    sphereBuffer.SetData(spheres);
  }

  private void Awake() {
    camera = GetComponent<Camera>();
  }

  private void Update() {
    if (transform.hasChanged) {
      currentSample = 0;
      transform.hasChanged = false;
    }

    if (DirectionalLight.transform.hasChanged) {
      currentSample = 0;
      DirectionalLight.transform.hasChanged = false;
    }

  }

  private void SetShaderParameters() {
    RayTracingShader.SetMatrix("_CameraToWorld", camera.cameraToWorldMatrix);
    RayTracingShader.SetMatrix("_CameraInverseProjection", camera.projectionMatrix.inverse);
    RayTracingShader.SetTexture(0, "_SkyboxTexture", SkyboxTexture);
    RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));

    Vector3 l = DirectionalLight.transform.forward;
    RayTracingShader.SetVector("_DirectionalLight", new Vector4(l.x, l.y, l.z, DirectionalLight.intensity));

    SetComputeBuffer("_Spheres", sphereBuffer);
    SetComputeBuffer("_MeshObjects", meshObjectBuffer);
    SetComputeBuffer("_Vertices", vertexBuffer);
    SetComputeBuffer("_Indices", indexBuffer);
    RayTracingShader.SetFloat("_Seed", Random.value);
  }

  // Called when camera finished rendering
  private void OnRenderImage(RenderTexture source, RenderTexture destination) {
    RebuildMeshObjectBuffers();
    SetShaderParameters();
    Render(destination);
  }

  private void Render(RenderTexture destination) {
    // Make sure we have a current render target
    InitRenderTexture(ref target);
    InitRenderTexture(ref converged);

    // Set the target and dispatch the compute shader
    RayTracingShader.SetTexture(0, "Result", target);
    int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
    int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
    RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

    // Blit the result texture to the screen
    if (addMaterial == null)
      addMaterial = new Material(Shader.Find("Hidden/AddShader"));

    addMaterial.SetFloat("_Sample", currentSample);
    Graphics.Blit(target, converged, addMaterial);
    Graphics.Blit(converged, destination);
    currentSample++;
  }

  private void InitRenderTexture(ref RenderTexture texture) {
    if (texture == null || texture.width != Screen.width || texture.height != Screen.height) {
      // Release render texture if we already have one
      if (texture != null)
        texture.Release();

      // Get a render target for Ray Tracing
      texture = new RenderTexture(Screen.width, Screen.height, 0, 
        RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
      texture.enableRandomWrite = true;
      texture.Create();
    }
  }

  public static void RegisterObject(RayTracingObject obj) {
    rayTracingObjects.Add(obj);
    meshObjectsNeedRebuilding = true;
  }

  public static void UnregisterObject(RayTracingObject obj) {
    rayTracingObjects.Remove(obj);
    meshObjectsNeedRebuilding = true;
  }

  private void RebuildMeshObjectBuffers() {
    if (!meshObjectsNeedRebuilding) {
      return;
    }

    meshObjectsNeedRebuilding = false;
    currentSample = 0;

    // Clear all lists
    meshObjects.Clear();
    vertices.Clear();
    indices.Clear();

    // Loop over all objects and gather their data
    foreach (RayTracingObject obj in rayTracingObjects) {
      Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;

      // Add vertex data
      int firstVertex = vertices.Count;
      vertices.AddRange(mesh.vertices);

      // Add index data - if the vertex buffer wasn't empty before, the
      // indices need to be offset
      int firstIndex = indices.Count;
      var meshIndices = mesh.GetIndices(0);
      indices.AddRange(meshIndices.Select(index => index + firstVertex));

      // Add the object itself
      meshObjects.Add(new MeshObject()
      {
        localToWorldMatrix = obj.transform.localToWorldMatrix,
        indices_offset = firstIndex,
        indices_count = meshIndices.Length
      });
    }

    CreateComputeBuffer(ref meshObjectBuffer, meshObjects, Marshal.SizeOf<MeshObject>());
    CreateComputeBuffer(ref vertexBuffer, vertices, Marshal.SizeOf<Vector3>());
    CreateComputeBuffer(ref indexBuffer, indices, Marshal.SizeOf<int>());
  }

  private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride)
    where T : struct
  {
    // Do we already have a compute buffer?
    if (buffer != null) {
      // If no data or buffer doesn't match the given criteria, release it
      if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride) {
        buffer.Release();
        buffer = null;
      }
    }
    
    if (data.Count != 0) {
      // If the buffer has been released or wasn't there to
      // begin with, create it
      if (buffer == null) {
        buffer = new ComputeBuffer(data.Count, stride);
      }

      // Set data on the buffer
      buffer.SetData(data);
    }
  }

  private void SetComputeBuffer(string name, ComputeBuffer buffer) {
    if (buffer != null) {
      RayTracingShader.SetBuffer(0, name, buffer);
    }
  }
}
