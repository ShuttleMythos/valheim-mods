using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Knochenbrecher
{
    /// <summary>
    /// Ersetzt das Aussehen des geklonten Vorschlaghammers durch das eigene Modell
    /// (tools/make_hammer.py, eingebettet als hammer.bin).
    ///
    /// Das Modell wird zur Laufzeit an den Original-Hammer angepasst: gleiche Lage des Knaufs,
    /// gleiche Ausrichtung, gleiche Gesamtlänge. So sitzt es ohne Unity-Projekt richtig in der Hand.
    /// </summary>
    internal static class HammerModel
    {
        private const string ResourceName = "Knochenbrecher.hammer.bin";

        private class Part
        {
            public string Name;
            public Color Color;
            public float Metallic;
            public float Gloss;
            public float Emission;
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public int[] Triangles;
        }

        /// <returns>true, wenn das eigene Modell eingesetzt wurde.</returns>
        public static bool Apply(GameObject prefab, bool flip)
        {
            var root = prefab.transform.Find("attach") ?? prefab.transform;
            MeshFilter reference = FindReference(root) ?? FindReference(prefab.transform);
            if (reference == null)
            {
                Jotunn.Logger.LogWarning("Kein Modell am Vorschlaghammer gefunden – der Knochenbrecher behält das alte Aussehen.");
                return false;
            }

            var renderer = reference.GetComponent<MeshRenderer>();
            var parts = Load();

            // --- Lage des Original-Hammers bestimmen (im Koordinatensystem seines Meshes) ---
            Bounds bounds = reference.sharedMesh.bounds;
            Vector3 size = bounds.size;
            int a = LongestAxis(size, -1);     // Stiel
            int c = ShortestAxis(size, a);     // quer zum Kopf
            Vector3 hand = reference.transform.InverseTransformPoint(root.position);
            // Die Hand greift nahe am Knauf, der Kopf sitzt am weiter entfernten Ende
            bool headAtMax = Mathf.Abs(bounds.max[a] - hand[a]) >= Mathf.Abs(bounds.min[a] - hand[a]);
            if (flip)
            {
                headAtMax = !headAtMax;
            }

            Vector3 up = Axis(a) * (headAtMax ? 1f : -1f);
            Quaternion rotation = Quaternion.LookRotation(Axis(c), up);
            Vector3 origin = bounds.center;
            origin[a] = headAtMax ? bounds.min[a] : bounds.max[a];

            // --- eigenes Modell: Stiel entlang +Y, Knauf unten ---
            float minY = float.MaxValue, maxY = float.MinValue;
            int vertexCount = 0;
            foreach (var part in parts)
            {
                vertexCount += part.Vertices.Length;
                foreach (var v in part.Vertices)
                {
                    minY = Mathf.Min(minY, v.y);
                    maxY = Mathf.Max(maxY, v.y);
                }
            }
            float scale = size[a] / (maxY - minY);

            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var mesh = new Mesh { name = "knochenbrecher" };
            if (vertexCount > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            int offset = 0;
            var triangles = new List<int[]>();
            foreach (var part in parts)
            {
                for (int i = 0; i < part.Vertices.Length; i++)
                {
                    Vector3 v = part.Vertices[i];
                    v.y -= minY;
                    vertices[offset + i] = origin + rotation * (v * scale);
                    normals[offset + i] = (rotation * part.Normals[i]).normalized;
                }

                var shifted = new int[part.Triangles.Length];
                for (int i = 0; i < shifted.Length; i++)
                {
                    shifted[i] = part.Triangles[i] + offset;
                }
                triangles.Add(shifted);
                offset += part.Vertices.Length;
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.subMeshCount = parts.Count;
            for (int i = 0; i < parts.Count; i++)
            {
                mesh.SetTriangles(triangles[i], i);
            }
            mesh.RecalculateBounds();

            // --- Materialien: Kopien des Originalmaterials, damit Valheims Beleuchtung stimmt ---
            Material template = renderer.sharedMaterial;
            LogShader(template);
            var materials = new Material[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                materials[i] = CreateMaterial(template, parts[i]);
            }

            // Übrige Mesh-Teile des alten Modells ausblenden. Partikel, Licht und Schlagspur bleiben erhalten.
            foreach (var other in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (other != renderer)
                {
                    other.enabled = false;
                }
            }

            reference.sharedMesh = mesh;
            renderer.sharedMaterials = materials;
            Jotunn.Logger.LogInfo($"Eigenes Hammer-Modell eingesetzt ({vertexCount} Eckpunkte, {parts.Count} Materialien).");
            return true;
        }

        private static MeshFilter FindReference(Transform root)
        {
            MeshFilter best = null;
            float bestSize = 0f;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null)
                {
                    continue;
                }

                float magnitude = filter.sharedMesh.bounds.size.magnitude;
                if (magnitude > bestSize)
                {
                    best = filter;
                    bestSize = magnitude;
                }
            }
            return best;
        }

        private static Material CreateMaterial(Material template, Part part)
        {
            var material = new Material(template) { name = "knochenbrecher_" + part.Name };

            SetTexture(material, "_MainTex", Texture2D.whiteTexture);
            foreach (var texture in new[] { "_BumpMap", "_MetallicGlossMap", "_SpecGlossMap", "_EmissionMap", "_OcclusionMap", "_DetailAlbedoMap", "_DetailNormalMap" })
            {
                SetTexture(material, texture, null);
            }
            material.DisableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_NORMALMAP");

            SetColor(material, "_Color", part.Color);
            SetColor(material, "_MetalColor", part.Color);
            SetFloat(material, "_UseGlossmap", 0f);
            SetFloat(material, "_Metallic", part.Metallic);
            SetFloat(material, "_MetalGloss", part.Gloss);
            SetFloat(material, "_Glossiness", part.Gloss);
            SetFloat(material, "_Smoothness", part.Gloss);
            SetFloat(material, "_GlossMapScale", part.Gloss);

            if (part.Emission > 0f)
            {
                SetColor(material, "_EmissionColor", part.Color * part.Emission);
                material.EnableKeyword("_EMISSION");
            }
            else
            {
                SetColor(material, "_EmissionColor", Color.black);
            }
            return material;
        }

        private static void SetTexture(Material material, string name, Texture value)
        {
            if (material.HasProperty(name))
            {
                material.SetTexture(name, value);
            }
        }

        private static void SetColor(Material material, string name, Color value)
        {
            if (material.HasProperty(name))
            {
                material.SetColor(name, value);
            }
        }

        private static void SetFloat(Material material, string name, float value)
        {
            if (material.HasProperty(name))
            {
                material.SetFloat(name, value);
            }
        }

        /// <summary>Schreibt Shader und Eigenschaften ins Log – hilft, falls das Metall im Spiel nicht wie gewünscht glänzt.</summary>
        private static void LogShader(Material template)
        {
            try
            {
                Shader shader = template.shader;
                var names = new List<string>();
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    names.Add(shader.GetPropertyName(i));
                }
                Jotunn.Logger.LogInfo($"Hammer-Shader: {shader.name} [{string.Join(", ", names)}]");
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogDebug($"Shader-Eigenschaften nicht lesbar: {e.Message}");
            }
        }

        private static Vector3 Axis(int index)
        {
            return index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;
        }

        private static int LongestAxis(Vector3 size, int except)
        {
            int best = -1;
            for (int i = 0; i < 3; i++)
            {
                if (i != except && (best < 0 || size[i] > size[best]))
                {
                    best = i;
                }
            }
            return best;
        }

        private static int ShortestAxis(Vector3 size, int except)
        {
            int best = -1;
            for (int i = 0; i < 3; i++)
            {
                if (i != except && (best < 0 || size[i] < size[best]))
                {
                    best = i;
                }
            }
            return best;
        }

        private static List<Part> Load()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Eingebettetes Modell fehlt: " + ResourceName);
                }

                using (var reader = new BinaryReader(stream))
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "KBH1")
                    {
                        throw new InvalidDataException("hammer.bin hat ein unbekanntes Format.");
                    }

                    var parts = new List<Part>();
                    int count = reader.ReadInt32();
                    for (int p = 0; p < count; p++)
                    {
                        var part = new Part { Name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())) };
                        part.Color = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 1f);
                        part.Metallic = reader.ReadSingle();
                        part.Gloss = reader.ReadSingle();
                        part.Emission = reader.ReadSingle();

                        int vertexCount = reader.ReadInt32();
                        part.Vertices = new Vector3[vertexCount];
                        part.Normals = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            part.Vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            part.Normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        }

                        int indexCount = reader.ReadInt32();
                        part.Triangles = new int[indexCount];
                        for (int i = 0; i < indexCount; i++)
                        {
                            part.Triangles[i] = reader.ReadInt32();
                        }
                        parts.Add(part);
                    }
                    return parts;
                }
            }
        }
    }
}
