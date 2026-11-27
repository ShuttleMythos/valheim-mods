// Minimaler Ersatz für die benutzten Unity-Klassen, damit HammerModel.cs unverändert außerhalb des Spiels läuft.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jotunn
{
    public static class Logger
    {
        public static List<string> Lines = new List<string>();
        public static void LogInfo(object o) => Lines.Add("INFO " + o);
        public static void LogWarning(object o) => Lines.Add("WARN " + o);
        public static void LogDebug(object o) => Lines.Add("DEBUG " + o);
    }
}

namespace UnityEngine.Rendering { public enum IndexFormat { UInt16, UInt32 } }

namespace UnityEngine
{
    public static class Mathf
    {
        public static float Abs(float v) => Math.Abs(v);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float this[int i]
        {
            get => i == 0 ? x : i == 1 ? y : z;
            set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
        }
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-9f ? this * (1f / m) : new Vector3(0, 0, 0); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }

    /// <summary>Drehung als drei Bildvektoren der Achsen (reicht für LookRotation und Drehen von Vektoren).</summary>
    public struct Quaternion
    {
        public Vector3 X, Y, Z;
        public static Quaternion identity => new Quaternion { X = Vector3.right, Y = Vector3.up, Z = Vector3.forward };
        // Wie Unity: Z -> forward, X -> up x forward, Y -> forward x X
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
        {
            Vector3 f = forward.normalized;
            Vector3 r = Vector3.Cross(upwards, f).normalized;
            return new Quaternion { X = r, Y = Vector3.Cross(f, r), Z = f };
        }
        public static Vector3 operator *(Quaternion q, Vector3 v) => q.X * v.x + q.Y * v.y + q.Z * v.z;
        public Vector3 Inverse(Vector3 v) => new Vector3(Vector3.Dot(X, v), Vector3.Dot(Y, v), Vector3.Dot(Z, v));
    }

    public struct Bounds
    {
        public Vector3 min, max;
        public Vector3 center => (min + max) * 0.5f;
        public Vector3 size => max - min;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black => new Color(0, 0, 0, 1);
        public static Color operator *(Color c, float s) => new Color(c.r * s, c.g * s, c.b * s, c.a * s);
    }

    public class Object { public string name; }
    public class Texture : Object { }
    public class Texture2D : Texture { public static Texture2D whiteTexture = new Texture2D(); }

    public class Shader : Object
    {
        public List<string> Props = new List<string>();
        public int GetPropertyCount() => Props.Count;
        public string GetPropertyName(int i) => Props[i];
    }

    public class Material : Object
    {
        public Shader shader;
        public Dictionary<string, object> Values = new Dictionary<string, object>();
        public HashSet<string> Keywords = new HashSet<string>();
        public Material(Material source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            shader = source.shader; Values = new Dictionary<string, object>(source.Values); Keywords = new HashSet<string>(source.Keywords);
        }
        public Material(Shader s) { shader = s; }
        public bool HasProperty(string n) => shader.Props.Contains(n);
        public void SetTexture(string n, Texture t) => Values[n] = t;
        public void SetColor(string n, Color c) => Values[n] = c;
        public void SetFloat(string n, float f) => Values[n] = f;
        public void EnableKeyword(string k) => Keywords.Add(k);
        public void DisableKeyword(string k) => Keywords.Remove(k);
    }

    public class Mesh : Object
    {
        public Bounds bounds;
        public Vector3[] vertices, normals;
        public int subMeshCount;
        public Rendering.IndexFormat indexFormat;
        public Dictionary<int, int[]> Triangles = new Dictionary<int, int[]>();
        public void SetTriangles(int[] t, int sub) { if (sub >= subMeshCount) throw new IndexOutOfRangeException(); Triangles[sub] = t; }
        public void RecalculateBounds()
        {
            var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var mx = mn * -1f;
            foreach (var v in vertices) for (int i = 0; i < 3; i++) { mn[i] = Math.Min(mn[i], v[i]); mx[i] = Math.Max(mx[i], v[i]); }
            bounds = new Bounds { min = mn, max = mx };
        }
    }

    public class GameObject : Object
    {
        public Transform transform;
        public List<Component> Components = new List<Component>();
        public GameObject(string n, Transform parent = null)
        {
            name = n; transform = new Transform { gameObject = this, parent = parent }; transform.transform = transform;
            parent?.Children.Add(transform);
        }
        public T Add<T>() where T : Component, new() { var c = new T { gameObject = this, transform = transform }; Components.Add(c); return c; }
    }

    public class Component : Object
    {
        public GameObject gameObject; public Transform transform;
        public T GetComponent<T>() where T : Component => gameObject.Components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var list = new List<T>(); Collect(transform, list); return list.ToArray();
        }
        private static void Collect<T>(Transform t, List<T> list) where T : Component
        {
            list.AddRange(t.gameObject.Components.OfType<T>());
            foreach (var c in t.Children) Collect(c, list);
        }
    }

    public class Transform : Component
    {
        public Transform parent; public List<Transform> Children = new List<Transform>();
        public Vector3 localPosition; public Quaternion localRotation = Quaternion.identity; public float localScale = 1f;
        public Transform Find(string n) => Children.FirstOrDefault(c => c.gameObject.name == n);
        public Vector3 TransformPoint(Vector3 p)
        {
            Vector3 w = localPosition + localRotation * (p * localScale);
            return parent != null ? parent.TransformPoint(w) : w;
        }
        public Vector3 InverseTransformPoint(Vector3 w)
        {
            if (parent != null) w = parent.InverseTransformPoint(w);
            return localRotation.Inverse(w - localPosition) * (1f / localScale);
        }
        public Vector3 position => TransformPoint(new Vector3(0, 0, 0));
    }

    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class Renderer : Component
    {
        public bool enabled = true; public Material[] sharedMaterials = new Material[0];
        public Material sharedMaterial { get => sharedMaterials.Length > 0 ? sharedMaterials[0] : null; set => sharedMaterials = new[] { value }; }
    }
    public class MeshRenderer : Renderer { }
    public class ParticleSystemRenderer : Renderer { }
}
