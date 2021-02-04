using System.Collections.Generic;
using System.Runtime.InteropServices;
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

  public struct Sphere {
    public Vector3 position;
    public float radius;
    public Vector3 albedo;
    public Vector3 specular;
  };

  private void OnEnable() {
    currentSample = 0;
    SetUpScene();
  }

  private void OnDisable() {
    if (sphereBuffer != null)
      sphereBuffer.Release();
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
        bool metal = Random.value < 0.5f;
        sphere.albedo = metal ? Vector3.zero : new Vector3(color.r, color.g, color.b);
        sphere.specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.04f;

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

    RayTracingShader.SetBuffer(0, "_Spheres", sphereBuffer);
  }

  // Called when camera finished rendering
  private void OnRenderImage(RenderTexture source, RenderTexture destination) {
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
}
