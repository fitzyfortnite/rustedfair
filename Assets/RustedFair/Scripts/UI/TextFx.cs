using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RustedFair
{
    /// <summary>
    /// v1.6: letter spacing (tracking, in em) for legacy UI Text, which has none built in. Works on the generated glyph
    /// quads line by line and keeps the line's alignment. Glyph groups duplicated by Shadow / Outline (which restart
    /// at the left of a line) are handled as separate runs, so the effect order on the object doesn't matter.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class LetterSpacing : BaseMeshEffect
    {
        public float em = 0.16f;
        Text text;
        readonly List<UIVertex> verts = new List<UIVertex>();

        public static LetterSpacing Add(Text t, float em)
        {
            var ls = t.GetComponent<LetterSpacing>();
            if (ls == null) ls = t.gameObject.AddComponent<LetterSpacing>();
            ls.em = em;
            if (ls.graphic != null) ls.graphic.SetVerticesDirty();
            return ls;
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount < 8 || Mathf.Approximately(em, 0f)) return;
            if (text == null) text = GetComponent<Text>();
            if (text == null) return;
            float size = text.fontSize;
            if (text.resizeTextForBestFit && text.cachedTextGenerator != null && text.cachedTextGenerator.fontSizeUsedForBestFit > 0)
                size = text.cachedTextGenerator.fontSizeUsedForBestFit / Mathf.Max(0.0001f, text.pixelsPerUnit);
            float spacing = em * size;

            verts.Clear();
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); verts.Add(v); }
            int quads = verts.Count / 4;
            int runStart = 0;
            float runY = 0f, prevX = 0f;
            for (int q = 0; q <= quads; q++)
            {
                bool split = q == quads;
                float cx = 0f, minY = 0f;
                if (!split)
                {
                    cx = (verts[q * 4].position.x + verts[q * 4 + 2].position.x) * 0.5f;
                    minY = Mathf.Min(verts[q * 4].position.y, verts[q * 4 + 2].position.y);
                    if (q > runStart && (Mathf.Abs(minY - runY) > size * 0.6f || cx < prevX - size * 0.25f)) split = true;
                }
                if (split && q > runStart) ApplyRun(runStart, q, spacing);
                if (q == quads) break;
                if (split || q == runStart) { runStart = q; runY = minY; }
                prevX = cx;
            }
            for (int i = 0; i < verts.Count; i++) vh.SetUIVertex(verts[i], i);
        }

        void ApplyRun(int start, int end, float spacing)
        {
            int n = end - start;
            float total = (n - 1) * spacing, shift;
            switch (text.alignment)
            {
                case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: shift = 0f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: shift = total; break;
                default: shift = total * 0.5f; break;
            }
            for (int k = 0; k < n; k++)
            {
                float dx = k * spacing - shift;
                for (int j = 0; j < 4; j++)
                {
                    int i = (start + k) * 4 + j;
                    var vv = verts[i];
                    vv.position.x += dx;
                    verts[i] = vv;
                }
            }
        }
    }

    /// <summary>v1.6: vertical three-stop colour gradient over a UI graphic (the menu logo's "top-lit" red).</summary>
    public class VerticalGradient : BaseMeshEffect
    {
        public Color top = Color.white, mid = Color.gray, bottom = Color.black;
        [Range(0.05f, 0.95f)] public float midAt = 0.45f; // from the top
        readonly List<UIVertex> verts = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            verts.Clear();
            var v = new UIVertex();
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                verts.Add(v);
                minY = Mathf.Min(minY, v.position.y);
                maxY = Mathf.Max(maxY, v.position.y);
            }
            float h = Mathf.Max(0.001f, maxY - minY);
            for (int i = 0; i < verts.Count; i++)
            {
                var vv = verts[i];
                float t = (maxY - vv.position.y) / h; // 0 top .. 1 bottom
                Color c = t < midAt ? Color.Lerp(top, mid, t / midAt) : Color.Lerp(mid, bottom, (t - midAt) / (1f - midAt));
                Color baseC = vv.color;
                vv.color = new Color(c.r * baseC.r, c.g * baseC.g, c.b * baseC.b, c.a * baseC.a);
                verts[i] = vv;
                vh.SetUIVertex(vv, i);
            }
        }
    }
}
