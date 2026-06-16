using UnityEngine;
using UnityEditor;

public class MeshGeneratorWindow : EditorWindow
{
    [MenuItem("re-Gravity/Mesh Generator")]
    public static void ShowWindow()
    {
        GetWindow<MeshGeneratorWindow>("re-Gravity Mesh Gen");
    }

    private void OnGUI()
    {
        GUILayout.Label("Generate Base Meshes", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate Body Impostor Mesh (65536 Quads)"))
        {
            GenerateBodyMesh();
        }

        if (GUILayout.Button("Generate Trail Line Mesh (64x256)"))
        {
            GenerateTrailMesh();
        }
    }

    private void GenerateBodyMesh()
    {
        int count = 65536;
        int size = 256; // 256x256 texture

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // >65535 vertices

        Vector3[] vertices = new Vector3[count * 4];
        Vector2[] uv = new Vector2[count * 4];
        Vector2[] uv2 = new Vector2[count * 4]; // Stores (U,V) lookup
        int[] triangles = new int[count * 6];

        for (int i = 0; i < count; i++)
        {
            int x = i % size;
            int y = i / size;

            // Texture coordinate for center of the pixel
            float u = (x + 0.5f) / size;
            float v = (y + 0.5f) / size;

            int vIndex = i * 4;
            // Local quad positions (will be offset in shader)
            vertices[vIndex + 0] = new Vector3(-0.5f, -0.5f, 0);
            vertices[vIndex + 1] = new Vector3(-0.5f,  0.5f, 0);
            vertices[vIndex + 2] = new Vector3( 0.5f,  0.5f, 0);
            vertices[vIndex + 3] = new Vector3( 0.5f, -0.5f, 0);

            // Standard UV for rendering the quad itself
            uv[vIndex + 0] = new Vector2(0, 0);
            uv[vIndex + 1] = new Vector2(0, 1);
            uv[vIndex + 2] = new Vector2(1, 1);
            uv[vIndex + 3] = new Vector2(1, 0);

            // UV2 to read from the CRT
            Vector2 lookupUV = new Vector2(u, v);
            uv2[vIndex + 0] = lookupUV;
            uv2[vIndex + 1] = lookupUV;
            uv2[vIndex + 2] = lookupUV;
            uv2[vIndex + 3] = lookupUV;

            int tIndex = i * 6;
            triangles[tIndex + 0] = vIndex + 0;
            triangles[tIndex + 1] = vIndex + 1;
            triangles[tIndex + 2] = vIndex + 2;
            triangles[tIndex + 3] = vIndex + 0;
            triangles[tIndex + 4] = vIndex + 2;
            triangles[tIndex + 5] = vIndex + 3;
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.uv2 = uv2;
        mesh.triangles = triangles;
        // Massive bounds to prevent Unity from frustum culling the mesh when looking away
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(1000000, 1000000, 1000000));
        // Create directory if not exists
        if (!AssetDatabase.IsValidFolder("Assets/Models"))
        {
            AssetDatabase.CreateFolder("Assets", "Models");
        }

        AssetDatabase.CreateAsset(mesh, "Assets/Models/BodyImpostorMesh.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("Generated Body Impostor Mesh at Assets/Models/BodyImpostorMesh.asset");
    }

    private void GenerateTrailMesh()
    {
        int trails = 64;
        int points = 256;

        Mesh mesh = new Mesh();
        // 64 trails, each is a ribbon of 256 segments. A segment needs 2 vertices.
        // Total vertices = 64 * 256 * 2 = 32768
        
        Vector3[] vertices = new Vector3[trails * points * 2];
        Vector2[] uv = new Vector2[trails * points * 2]; // X: distance 0-1, Y: width -1 to 1
        Vector2[] uv2 = new Vector2[trails * points * 2]; // X: trail ID 0-63, Y: point ID 0-255
        int[] triangles = new int[trails * (points - 1) * 6];

        int vIndex = 0;
        int tIndex = 0;

        for (int t = 0; t < trails; t++)
        {
            for (int p = 0; p < points; p++)
            {
                // Top vertex
                vertices[vIndex] = Vector3.zero;
                uv[vIndex] = new Vector2((float)p / (points - 1), 1f);
                uv2[vIndex] = new Vector2(t, p);
                
                // Bottom vertex
                vertices[vIndex + 1] = Vector3.zero;
                uv[vIndex + 1] = new Vector2((float)p / (points - 1), -1f);
                uv2[vIndex + 1] = new Vector2(t, p);

                if (p < points - 1)
                {
                    int currentTop = vIndex;
                    int currentBottom = vIndex + 1;
                    int nextTop = vIndex + 2;
                    int nextBottom = vIndex + 3;

                    triangles[tIndex + 0] = currentBottom;
                    triangles[tIndex + 1] = currentTop;
                    triangles[tIndex + 2] = nextTop;
                    
                    triangles[tIndex + 3] = currentBottom;
                    triangles[tIndex + 4] = nextTop;
                    triangles[tIndex + 5] = nextBottom;

                    tIndex += 6;
                }

                vIndex += 2;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.uv2 = uv2;
        mesh.triangles = triangles;
        
        // Large bounds so it's never culled when moving
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000, 100000, 100000));

        if (!AssetDatabase.IsValidFolder("Assets/Models"))
        {
            AssetDatabase.CreateFolder("Assets", "Models");
        }

        AssetDatabase.CreateAsset(mesh, "Assets/Models/TrailLineMesh.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("Generated Trail Line Mesh at Assets/Models/TrailLineMesh.asset");
    }
}
