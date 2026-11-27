using System;
using System.Linq;
using Knochenbrecher;
using UnityEngine;

static class Test
{
    static int failures, checks;
    static void Check(bool ok, string what) { checks++; if (!ok) { failures++; Console.WriteLine("  FEHLER: " + what); } }
    static Vector3 Unit(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    static ParticleSystemRenderer Particles;
    const float Len = 1.2f, HeadLen = 0.4f, Thick = 0.2f, Grip = 0.3f;

    static (GameObject prefab, MeshFilter filter, MeshRenderer renderer, Renderer extra, Mesh original) Build(int a, int b, bool headAtMax, bool withMaterial = true)
    {
        int c = 3 - a - b;
        var min = new Vector3(0.37f, -0.81f, 0.52f);            // absichtlich nicht um den Ursprung
        var max = min; max[a] += Len; max[b] += HeadLen; max[c] += Thick;
        var original = new Mesh { name = "sledge", bounds = new Bounds { min = min, max = max }, vertices = new Vector3[0] };

        var prefab = new GameObject("SledgeKnochenbrecher");
        var attach = new GameObject("attach", prefab.transform);
        attach.transform.localPosition = new Vector3(0.1f, 0.2f, -0.3f);
        var model = new GameObject("model", attach.transform);
        model.transform.localRotation = Quaternion.LookRotation(new Vector3(0.3f, -0.5f, 0.8f), new Vector3(0.2f, 1f, 0.1f));
        model.transform.localScale = 0.8f;
        // Griffpunkt (Hand) im Mesh: 0,3 vom Knauf entfernt, mittig
        Vector3 hand = (min + max) * 0.5f;
        hand[a] = headAtMax ? min[a] + Grip : max[a] - Grip;
        model.transform.localPosition = (model.transform.localRotation * (hand * 0.8f)) * -1f;

        var filter = model.Add<MeshFilter>(); filter.sharedMesh = original;
        var renderer = model.Add<MeshRenderer>();
        if (withMaterial)
        {
            var shader = new Shader { name = "Custom/Creature" };
            shader.Props.AddRange(new[] { "_Color", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_Metallic", "_Glossiness", "_EmissionColor" });
            renderer.sharedMaterial = new Material(shader) { name = "sledge_mat" };
        }

        var deco = new GameObject("deko", attach.transform);
        deco.Add<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds { min = new Vector3(0, 0, 0), max = new Vector3(0.05f, 0.05f, 0.05f) } };
        var extra = deco.Add<MeshRenderer>();
        extra.sharedMaterial = renderer.sharedMaterial;
        Particles = new GameObject("funken", attach.transform).Add<ParticleSystemRenderer>();
        return (prefab, filter, renderer, extra, original);
    }

    static void Scenario(int a, int b, bool headAtMax, bool flip)
    {
        int c = 3 - a - b;
        Console.WriteLine($"Stiel-Achse {"XYZ"[a]}, Kopf-Achse {"XYZ"[b]}, Kopf am {(headAtMax ? "oberen" : "unteren")} Ende{(flip ? ", umgedreht" : "")}");
        var (prefab, filter, renderer, extra, original) = Build(a, b, headAtMax);
        Bounds ob = original.bounds;
        bool ok = HammerModel.Apply(prefab, flip);
        Check(ok, "Apply meldet Misserfolg");
        Mesh mesh = filter.sharedMesh;
        Check(!ReferenceEquals(mesh, original), "Original-Mesh wurde nicht ersetzt");
        Bounds nb = mesh.bounds;
        bool expectHeadAtMax = headAtMax ^ flip;

        Check(Math.Abs(nb.size[a] - Len) < 1e-3, $"Länge {nb.size[a]} statt {Len}");
        Check(nb.size[a] > nb.size[b] && nb.size[b] > nb.size[c], $"Achsen vertauscht: {nb.size.x} {nb.size.y} {nb.size.z}");
        float pommelOrig = expectHeadAtMax ? ob.min[a] : ob.max[a];
        float pommelNew = expectHeadAtMax ? nb.min[a] : nb.max[a];
        Check(Math.Abs(pommelOrig - pommelNew) < 1e-3, $"Knauf liegt bei {pommelNew} statt {pommelOrig}");
        Check(Math.Abs(nb.center[b] - ob.center[b]) < 0.01 && Math.Abs(nb.center[c] - ob.center[c]) < 0.01, "Hammer sitzt nicht mittig");

        // Kopf (breit) muss am erwarteten Ende liegen
        float lo = nb.min[a], hi = nb.max[a];
        float Width(float from, float to)
        {
            var vs = mesh.vertices.Where(v => v[a] >= lo + from * (hi - lo) && v[a] <= lo + to * (hi - lo)).ToArray();
            return vs.Max(v => v[b]) - vs.Min(v => v[b]);
        }
        float top = Width(0.8f, 1f), bottom = Width(0f, 0.2f);
        Check(expectHeadAtMax ? top > 3 * bottom : bottom > 3 * top, $"Kopf am falschen Ende (Breite oben {top}, unten {bottom})");

        // Die Hand greift am Stiel, nicht im Kopf und nicht außerhalb des Hammers
        Vector3 hand = filter.transform.InverseTransformPoint(prefab.transform.Find("attach").position);
        float fromPommel = Math.Abs(hand[a] - pommelNew);
        if (!flip) Check(Math.Abs(fromPommel - Grip) < 1e-3, $"Hand {fromPommel} vom Knauf statt {Grip}");

        // Dreiecke zeigen nach außen, Normalen haben Länge 1
        int wrong = 0, total = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] t = mesh.Triangles[s];
            Check(t.Length % 3 == 0 && t.All(i => i >= 0 && i < mesh.vertices.Length), $"Teil {s}: ungültige Indizes");
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 v0 = mesh.vertices[t[i]], v1 = mesh.vertices[t[i + 1]], v2 = mesh.vertices[t[i + 2]];
                Vector3 n = mesh.normals[t[i]] + mesh.normals[t[i + 1]] + mesh.normals[t[i + 2]];
                total++;
                if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), n) <= 0) wrong++;
            }
        }
        Check(wrong == 0, $"{wrong} von {total} Dreiecken zeigen nach innen");
        Check(mesh.normals.All(n => Math.Abs(n.magnitude - 1f) < 1e-3), "Normalen nicht normiert");

        Check(mesh.subMeshCount == 8 && renderer.sharedMaterials.Length == 8, "nicht 8 Materialien");
        Check(renderer.sharedMaterials.All(m => m != null && m.name.StartsWith("knochenbrecher_") && m.Values.ContainsKey("_Color")), "Material unvollständig");
        var silver = renderer.sharedMaterials.First(m => m.name == "knochenbrecher_silver");
        Check((float)silver.Values["_Metallic"] == 1f && (float)silver.Values["_Glossiness"] > 0.85f, "Silber nicht metallisch/glänzend");
        Check(ReferenceEquals(silver.Values["_MainTex"], Texture2D.whiteTexture) && silver.Values["_BumpMap"] == null, "alte Texturen nicht entfernt");
        var ruby = renderer.sharedMaterials.First(m => m.name == "knochenbrecher_ruby");
        var rc = (Color)ruby.Values["_Color"];
        Check(rc.r > 0.8f && rc.g < 0.1f && ruby.Keywords.Contains("_EMISSION"), "Rubin nicht rot/leuchtend");
        Check(renderer.enabled && !extra.enabled, "alte Zusatzteile nicht ausgeblendet");
        Check(Particles.enabled, "Partikel-Effekt wurde ausgeblendet");
    }

    static int Main()
    {
        for (int a = 0; a < 3; a++)
            for (int b = 0; b < 3; b++)
                if (b != a)
                    foreach (bool head in new[] { true, false })
                        Scenario(a, b, head, false);
        Scenario(1, 0, true, true);

        Console.WriteLine("Fehlerfall: Original ohne Material");
        var (prefab, filter, renderer, extra, original) = Build(1, 0, true, withMaterial: false);
        bool threw = false;
        try { HammerModel.Apply(prefab, false); } catch (Exception) { threw = true; }
        Check(threw, "kein Fehler gemeldet");
        Check(ReferenceEquals(filter.sharedMesh, original) && extra.enabled, "Hammer wurde trotz Fehler verändert");

        Console.WriteLine("Fehlerfall: Prefab ohne Modell");
        Check(!HammerModel.Apply(new GameObject("leer"), false), "Apply meldet Erfolg ohne Modell");

        Console.WriteLine(Jotunn.Logger.Lines.First(l => l.Contains("Hammer-Shader")));
        Console.WriteLine(Jotunn.Logger.Lines.First(l => l.Contains("eingesetzt")));
        Console.WriteLine($"\n{checks} Prüfungen, {failures} Fehler");
        return failures == 0 ? 0 : 1;
    }
}
