using UnityEngine;
using System.Collections.Generic;

namespace ScamWithYourFriendsInVR.UI
{
    // Read the presenter's authored UVs rather than assuming its mesh is a quad
    // or adding colliders to native game objects.
    internal sealed class VRMeshPanelMapping
    {
        private readonly MeshRenderer renderer;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly string textureProperty;
        private readonly Vector3[] vertices;
        private readonly Vector2[] uv;
        private readonly int[] triangles;
        private readonly int slot;
        private readonly List<Material> materials = new List<Material>();
        private int dragTriangle = -1;

        internal VRMeshPanelMapping(MeshRenderer renderer, Mesh mesh, Material material, string property, int submesh)
        {
            this.renderer = renderer; this.mesh = mesh; this.material = material; textureProperty = property;
            slot = submesh;
            vertices = mesh.vertices; uv = mesh.uv; triangles = mesh.GetTriangles(submesh);
        }

        internal bool Active(RenderTexture texture) => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && SourceMatches(texture);

        internal bool SourceMatches(RenderTexture texture)
        {
            if (renderer == null || material == null || material.GetTexture(textureProperty) != texture
                || renderer.GetComponent<MeshFilter>()?.sharedMesh != mesh) return false;
            renderer.GetSharedMaterials(materials);
            return slot < materials.Count && materials[slot] == material;
        }

        internal bool Intersect(Ray ray, out Vector3 point, out Vector2 position, out float distance, bool bounded)
        {
            point = default; position = default; distance = 10f;
            Matrix4x4 inverse = renderer.transform.worldToLocalMatrix;
            Vector3 origin = inverse.MultiplyPoint3x4(ray.origin), direction = inverse.MultiplyVector(ray.direction);
            int picked = -1;
            Vector2 rawUV = default;
            int first = bounded ? 0 : dragTriangle;
            if (first < 0) return false;
            int last = bounded ? triangles.Length : first + 3;
            for (int i = first; i + 2 < last; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length
                    || a >= uv.Length || b >= uv.Length || c >= uv.Length) continue;
                Vector3 edge1 = vertices[b] - vertices[a], edge2 = vertices[c] - vertices[a];
                Vector3 cross = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, cross);
                if (Mathf.Abs(determinant) < 0.00000001f) continue;
                Vector3 fromA = origin - vertices[a];
                float u = Vector3.Dot(fromA, cross) / determinant;
                Vector3 q = Vector3.Cross(fromA, edge1);
                float v = Vector3.Dot(direction, q) / determinant;
                if (bounded && (u < 0 || v < 0 || u + v > 1)) continue;
                float hit = Vector3.Dot(edge2, q) / determinant;
                if (float.IsNaN(hit) || float.IsInfinity(hit) || hit < 0 || hit > distance) continue;
                distance = hit; picked = i;
                rawUV = uv[a] * (1 - u - v) + uv[b] * u + uv[c] * v;
            }
            if (picked < 0) return false;
            position = Vector2.Scale(rawUV, material.GetTextureScale(textureProperty)) + material.GetTextureOffset(textureProperty);
            if (bounded && (position.x < 0 || position.x > 1 || position.y < 0 || position.y > 1)) return false;
            if (bounded) dragTriangle = picked;
            point = ray.GetPoint(distance);
            return true;
        }
    }
}
