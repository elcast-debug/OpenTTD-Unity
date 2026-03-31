using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Static utility that procedurally generates Unity <see cref="Mesh"/> objects for
    /// every <see cref="RailDirection"/> variant.
    ///
    /// Rail anatomy (per tile):
    /// <list type="bullet">
    ///   <item>Two parallel rails (thin, flat boxes) running the length of the tile.</item>
    ///   <item>Evenly-spaced sleepers/ties (thin, wide boxes) underneath the rails.</item>
    ///   <item>Curves use arc sub-divisions with configurable segment count.</item>
    /// </list>
    ///
    /// Coordinate convention: one tile = 1 Unity unit.  Rail sits at Y = 0 (caller
    /// offsets above terrain).  Tile centre is at local (0, 0, 0).
    ///
    /// Materials:
    /// <list type="bullet">
    ///   <item>Rail beams — dark grey (<c>#3A3A3A</c>).</item>
    ///   <item>Sleepers   — brown    (<c>#6B4226</c>).</item>
    /// </list>
    /// </summary>
    public static class RailMeshGenerator
    {
        // ── Geometry constants ──────────────────────────────────────────────

        /// <summary>Half the gauge (distance from tile centre-line to each rail beam).</summary>
        private const float RailGaugeHalf = 0.18f;

        /// <summary>Rail beam cross-section width.</summary>
        private const float RailWidth = 0.04f;

        /// <summary>Rail beam cross-section height.</summary>
        private const float RailHeight = 0.04f;

        /// <summary>Sleeper width (spans across both rails, plus margin).</summary>
        private const float SleeperWidth = 0.50f;

        /// <summary>Sleeper thickness.</summary>
        private const float SleeperHeight = 0.025f;

        /// <summary>Sleeper depth along the rail direction.</summary>
        private const float SleeperDepth = 0.08f;

        /// <summary>Number of sleepers per straight tile.</summary>
        private const int SleepersPerTile = 6;

        /// <summary>Number of arc segments used to approximate a curve.</summary>
        private const int CurveSegments = 8;

        /// <summary>Tile half-size in Unity units.</summary>
        private const float TileHalf = 0.5f;

        // ── Cached material references ──────────────────────────────────────

        private static Material _railMat;
        private static Material _ghostMat;
        private static Material _bulldozeMat;

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Generates and returns a <see cref="Mesh"/> for the given <see cref="RailDirection"/>.
        /// The mesh is centred on the local origin (tile centre) and fits within a 1×1 unit footprint.
        /// </summary>
        /// <param name="direction">Rail direction / shape type.</param>
        /// <returns>A new <see cref="Mesh"/> instance.</returns>
        public static Mesh GenerateRailMesh(RailDirection direction)
        {
            switch (direction)
            {
                case RailDirection.North_South:
                    return BuildStraightMesh(isNS: true);

                case RailDirection.East_West:
                    return BuildStraightMesh(isNS: false);

                case RailDirection.Curve_NE:
                    return BuildCurveMesh(fromAngle: 180f, toAngle: 90f);   // W→N
                case RailDirection.Curve_NW:
                    return BuildCurveMesh(fromAngle: 0f,   toAngle: 90f);   // E→N
                case RailDirection.Curve_SE:
                    return BuildCurveMesh(fromAngle: 180f, toAngle: 270f);  // W→S
                case RailDirection.Curve_SW:
                    return BuildCurveMesh(fromAngle: 0f,   toAngle: 270f);  // E→S

                case RailDirection.Junction_T_N:
                    return BuildJunctionMesh(openN: true,  openS: false, openE: true, openW: true);
                case RailDirection.Junction_T_S:
                    return BuildJunctionMesh(openN: false, openS: true,  openE: true, openW: true);
                case RailDirection.Junction_T_E:
                    return BuildJunctionMesh(openN: true,  openS: true,  openE: true, openW: false);
                case RailDirection.Junction_T_W:
                    return BuildJunctionMesh(openN: true,  openS: true,  openE: false,openW: true);
                case RailDirection.Junction_Cross:
                    return BuildJunctionMesh(openN: true,  openS: true,  openE: true, openW: true);

                default:
                    Debug.LogWarning($"[RailMeshGenerator] Unknown direction {direction}, using North_South.");
                    return BuildStraightMesh(isNS: true);
            }
        }

        /// <summary>Returns (and caches) the shared dark-grey rail material.</summary>
        public static Material GetOrCreateRailMaterial()
        {
            if (_railMat != null) return _railMat;
            _railMat = CreateUnlitMaterial(new Color(0.23f, 0.23f, 0.23f)); // ~#3A3A3A
            _railMat.name = "Rail_Rail";
            return _railMat;
        }

        /// <summary>Returns (and caches) a semi-transparent blue ghost material.</summary>
        public static Material GetOrCreateGhostMaterial()
        {
            if (_ghostMat != null) return _ghostMat;
            var color = new Color(0.2f, 0.6f, 1f, 0.45f);
            _ghostMat = CreateUnlitMaterial(color, transparent: true);
            _ghostMat.name = "Rail_Ghost";
            return _ghostMat;
        }

        /// <summary>Returns (and caches) a semi-transparent red bulldoze material.</summary>
        public static Material GetOrCreateBulldozeMaterial()
        {
            if (_bulldozeMat != null) return _bulldozeMat;
            var color = new Color(1f, 0.2f, 0.2f, 0.55f);
            _bulldozeMat = CreateUnlitMaterial(color, transparent: true);
            _bulldozeMat.name = "Rail_Bulldoze";
            return _bulldozeMat;
        }

        // ── Straight mesh ───────────────────────────────────────────────────

        /// <summary>
        /// Builds a straight rail mesh oriented along the Z axis (N-S) or X axis (E-W).
        /// Consists of two rail beams and <see cref="SleepersPerTile"/> sleepers.
        /// </summary>
        private static Mesh BuildStraightMesh(bool isNS)
        {
            var verts  = new List<Vector3>();
            var tris   = new List<int>();
            var uvs    = new List<Vector2>();

            // ── Left rail ───────────────────────────────────────────────────
            // Centre of left rail: (–gauge, 0, 0) rotated by 90° if EW
            Vector3 leftCentre  = isNS
                ? new Vector3(-RailGaugeHalf, RailHeight * 0.5f, 0)
                : new Vector3(0, RailHeight * 0.5f, -RailGaugeHalf);

            Vector3 rightCentre = isNS
                ? new Vector3( RailGaugeHalf, RailHeight * 0.5f, 0)
                : new Vector3(0, RailHeight * 0.5f,  RailGaugeHalf);

            // Rail runs from –TileHalf to +TileHalf in either Z or X
            if (isNS)
            {
                AddBox(verts, tris, uvs, leftCentre,
                    new Vector3(RailWidth, RailHeight, 1f));
                AddBox(verts, tris, uvs, rightCentre,
                    new Vector3(RailWidth, RailHeight, 1f));
            }
            else
            {
                AddBox(verts, tris, uvs, leftCentre,
                    new Vector3(1f, RailHeight, RailWidth));
                AddBox(verts, tris, uvs, rightCentre,
                    new Vector3(1f, RailHeight, RailWidth));
            }

            // ── Sleepers ────────────────────────────────────────────────────
            float sleeperY = 0f;
            for (int i = 0; i < SleepersPerTile; i++)
            {
                float t = (i + 0.5f) / SleepersPerTile - 0.5f; // –0.5 … +0.5
                Vector3 sleeperCentre = isNS
                    ? new Vector3(0, sleeperY, t)
                    : new Vector3(t, sleeperY, 0);

                Vector3 sleeperSize = isNS
                    ? new Vector3(SleeperWidth, SleeperHeight, SleeperDepth)
                    : new Vector3(SleeperDepth, SleeperHeight, SleeperWidth);

                AddBox(verts, tris, uvs, sleeperCentre, sleeperSize);
            }

            return BuildMesh(verts, tris, uvs, $"RailMesh_{(isNS ? "NS" : "EW")}");
        }

        // ── Curve mesh ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds a curved rail mesh by sweeping rail cross-sections along a
        /// quarter-circle arc centred at a tile corner.
        /// </summary>
        /// <param name="fromAngle">Start angle in degrees (XZ plane, measured from +X).</param>
        /// <param name="toAngle">End angle in degrees.</param>
        private static Mesh BuildCurveMesh(float fromAngle, float toAngle)
        {
            var verts = new List<Vector3>();
            var tris  = new List<int>();
            var uvs   = new List<Vector2>();

            // Arc radius = tile size (curve sweeps from one edge midpoint to another)
            float arcRadius = TileHalf;

            // Corner of the arc (tile corner closest to the curve centre)
            // Determine corner from angle range
            // The centre of the arc sits at the tile corner "opposite" the opening
            Vector3 arcCentre = GetArcCentre(fromAngle, toAngle);

            int segs = CurveSegments;
            float angleStep = (toAngle - fromAngle) / segs;

            // Build two parallel rails and sleepers along the arc
            float[] gaugeOffsets = { -RailGaugeHalf, RailGaugeHalf };

            foreach (float gaugeOffset in gaugeOffsets)
            {
                float railRadius = arcRadius + gaugeOffset;
                for (int i = 0; i < segs; i++)
                {
                    float a0 = (fromAngle + i * angleStep)       * Mathf.Deg2Rad;
                    float a1 = (fromAngle + (i + 1) * angleStep) * Mathf.Deg2Rad;

                    Vector3 p0 = arcCentre + new Vector3(Mathf.Cos(a0) * railRadius, RailHeight * 0.5f, Mathf.Sin(a0) * railRadius);
                    Vector3 p1 = arcCentre + new Vector3(Mathf.Cos(a1) * railRadius, RailHeight * 0.5f, Mathf.Sin(a1) * railRadius);

                    // Extrude a small rectangular cross-section for each arc segment
                    Vector3 tangent = (p1 - p0).normalized;
                    Vector3 up      = Vector3.up;
                    Vector3 cross   = Vector3.Cross(up, tangent).normalized;

                    AddQuadPrism(verts, tris, uvs, p0, p1, cross, up,
                                 RailWidth * 0.5f, RailHeight * 0.5f);
                }
            }

            // Sleepers along the arc mid-radius
            for (int i = 0; i < SleepersPerTile; i++)
            {
                float t  = (i + 0.5f) / SleepersPerTile;
                float ang = (fromAngle + t * (toAngle - fromAngle)) * Mathf.Deg2Rad;

                Vector3 pos = arcCentre + new Vector3(Mathf.Cos(ang) * arcRadius, 0f, Mathf.Sin(ang) * arcRadius);

                // Orient sleeper perpendicular to arc tangent
                float tangentAng = ang + Mathf.PI * 0.5f;
                Vector3 sleeperDir = new Vector3(Mathf.Cos(tangentAng), 0, Mathf.Sin(tangentAng));

                AddOrientedBox(verts, tris, uvs, pos,
                               sleeperDir, Vector3.up,
                               SleeperWidth, SleeperHeight, SleeperDepth);
            }

            return BuildMesh(verts, tris, uvs,
                $"RailMesh_Curve_{fromAngle:F0}_{toAngle:F0}");
        }

        private static Vector3 GetArcCentre(float fromAngle, float toAngle)
        {
            // The arc is a quarter-circle.  The centre is at the tile corner
            // "inside" the curve.  Determine by the bisector of from/to angles.
            float bisector = (fromAngle + toAngle) * 0.5f * Mathf.Deg2Rad;
            // Opposite corner from the bisector direction
            float cx = -Mathf.Sign(Mathf.Cos(bisector)) * TileHalf;
            float cz = -Mathf.Sign(Mathf.Sin(bisector)) * TileHalf;
            return new Vector3(cx, 0, cz);
        }

        // ── Junction mesh ───────────────────────────────────────────────────

        /// <summary>
        /// Builds a junction mesh by combining straight segments for each open direction,
        /// overlapping at the tile centre.
        /// </summary>
        private static Mesh BuildJunctionMesh(bool openN, bool openS, bool openE, bool openW)
        {
            var verts = new List<Vector3>();
            var tris  = new List<int>();
            var uvs   = new List<Vector2>();

            bool ns = openN || openS;
            bool ew = openE || openW;

            float halfLength = TileHalf;

            // North–South rail beams
            if (ns)
            {
                float zStart = openS ? -halfLength : 0f;
                float zEnd   = openN ? halfLength  : 0f;
                AddRailBeam(verts, tris, uvs,
                    start: new Vector3(-RailGaugeHalf, RailHeight * 0.5f, zStart),
                    end:   new Vector3(-RailGaugeHalf, RailHeight * 0.5f, zEnd),
                    width: RailWidth, height: RailHeight);
                AddRailBeam(verts, tris, uvs,
                    start: new Vector3( RailGaugeHalf, RailHeight * 0.5f, zStart),
                    end:   new Vector3( RailGaugeHalf, RailHeight * 0.5f, zEnd),
                    width: RailWidth, height: RailHeight);
            }

            // East–West rail beams
            if (ew)
            {
                float xStart = openW ? -halfLength : 0f;
                float xEnd   = openE ?  halfLength : 0f;
                AddRailBeam(verts, tris, uvs,
                    start: new Vector3(xStart, RailHeight * 0.5f, -RailGaugeHalf),
                    end:   new Vector3(xEnd,   RailHeight * 0.5f, -RailGaugeHalf),
                    width: RailWidth, height: RailHeight);
                AddRailBeam(verts, tris, uvs,
                    start: new Vector3(xStart, RailHeight * 0.5f,  RailGaugeHalf),
                    end:   new Vector3(xEnd,   RailHeight * 0.5f,  RailGaugeHalf),
                    width: RailWidth, height: RailHeight);
            }

            // Sleepers — both axes
            if (ns)
                AddStraightSleepers(verts, tris, uvs, isNS: true);
            if (ew)
                AddStraightSleepers(verts, tris, uvs, isNS: false);

            return BuildMesh(verts, tris, uvs, "RailMesh_Junction");
        }

        // ── Geometry primitives ─────────────────────────────────────────────

        /// <summary>Adds an axis-aligned box centred at <paramref name="centre"/> with given size.</summary>
        private static void AddBox(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                   Vector3 centre, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            // 8 corners
            Vector3[] c =
            {
                centre + new Vector3(-h.x, -h.y, -h.z),
                centre + new Vector3( h.x, -h.y, -h.z),
                centre + new Vector3( h.x,  h.y, -h.z),
                centre + new Vector3(-h.x,  h.y, -h.z),
                centre + new Vector3(-h.x, -h.y,  h.z),
                centre + new Vector3( h.x, -h.y,  h.z),
                centre + new Vector3( h.x,  h.y,  h.z),
                centre + new Vector3(-h.x,  h.y,  h.z),
            };
            AddBoxFaces(verts, tris, uvs, c);
        }

        /// <summary>Adds a rail beam (thin box) from start to end along local Y-axis.</summary>
        private static void AddRailBeam(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                        Vector3 start, Vector3 end, float width, float height)
        {
            Vector3 dir    = (end - start).normalized;
            Vector3 up     = Vector3.up;
            Vector3 right  = Vector3.Cross(up, dir).normalized;
            float   length = Vector3.Distance(start, end);
            Vector3 centre = (start + end) * 0.5f;

            AddOrientedBox(verts, tris, uvs, centre, dir, up, width, height, length);
        }

        /// <summary>Adds sleepers distributed along a straight line.</summary>
        private static void AddStraightSleepers(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                                bool isNS)
        {
            for (int i = 0; i < SleepersPerTile; i++)
            {
                float t = (i + 0.5f) / SleepersPerTile - 0.5f;
                Vector3 centre = isNS ? new Vector3(0, 0, t) : new Vector3(t, 0, 0);
                Vector3 size   = isNS
                    ? new Vector3(SleeperWidth, SleeperHeight, SleeperDepth)
                    : new Vector3(SleeperDepth, SleeperHeight, SleeperWidth);
                AddBox(verts, tris, uvs, centre, size);
            }
        }

        /// <summary>Adds an oriented (non-axis-aligned) box.</summary>
        private static void AddOrientedBox(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                           Vector3 centre, Vector3 forward, Vector3 up,
                                           float width, float height, float depth)
        {
            Vector3 f = forward.normalized * depth  * 0.5f;
            Vector3 u = up.normalized      * height * 0.5f;
            Vector3 r = Vector3.Cross(forward, up).normalized * width * 0.5f;

            // Safety: if forward and up are parallel, pick an arbitrary right
            if (r.sqrMagnitude < 1e-6f)
                r = Vector3.Cross(forward, Vector3.right).normalized * width * 0.5f;

            Vector3[] c =
            {
                centre - r - u - f,
                centre + r - u - f,
                centre + r + u - f,
                centre - r + u - f,
                centre - r - u + f,
                centre + r - u + f,
                centre + r + u + f,
                centre - r + u + f,
            };
            AddBoxFaces(verts, tris, uvs, c);
        }

        /// <summary>Adds a quad prism (extruded rectangle) between two points.</summary>
        private static void AddQuadPrism(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                         Vector3 p0, Vector3 p1,
                                         Vector3 right, Vector3 up,
                                         float halfW, float halfH)
        {
            Vector3[] face0 =
            {
                p0 - right * halfW - up * halfH,
                p0 + right * halfW - up * halfH,
                p0 + right * halfW + up * halfH,
                p0 - right * halfW + up * halfH,
            };
            Vector3[] face1 =
            {
                p1 - right * halfW - up * halfH,
                p1 + right * halfW - up * halfH,
                p1 + right * halfW + up * halfH,
                p1 - right * halfW + up * halfH,
            };

            // Build 4 side quads + 2 end caps
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                AddQuad(verts, tris, uvs,
                    face0[i], face0[next], face1[next], face1[i]);
            }
            // End caps
            AddQuad(verts, tris, uvs, face0[0], face0[3], face0[2], face0[1]);
            AddQuad(verts, tris, uvs, face1[0], face1[1], face1[2], face1[3]);
        }

        private static void AddBoxFaces(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                        Vector3[] c)
        {
            // Front (+Z), Back (–Z), Left (–X), Right (+X), Top (+Y), Bottom (–Y)
            AddQuad(verts, tris, uvs, c[4], c[5], c[6], c[7]); // front
            AddQuad(verts, tris, uvs, c[1], c[0], c[3], c[2]); // back
            AddQuad(verts, tris, uvs, c[0], c[4], c[7], c[3]); // left
            AddQuad(verts, tris, uvs, c[5], c[1], c[2], c[6]); // right
            AddQuad(verts, tris, uvs, c[3], c[7], c[6], c[2]); // top
            AddQuad(verts, tris, uvs, c[0], c[1], c[5], c[4]); // bottom
        }

        private static void AddQuad(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                    Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));
            // Two triangles (CCW winding)
            tris.Add(start);     tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start);     tris.Add(start + 2); tris.Add(start + 3);
        }

        // ── Mesh assembly ───────────────────────────────────────────────────

        private static Mesh BuildMesh(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                                      string meshName)
        {
            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.Optimize();
            return mesh;
        }

        // ── Material helpers ────────────────────────────────────────────────

        private static Material CreateUnlitMaterial(Color color, bool transparent = false)
        {
            string shaderName = transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Unlit";
            var shader = Shader.Find(shaderName);
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { color = color };

            if (transparent)
            {
                mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent (URP)
                mat.SetFloat("_Blend", 0f);
                mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite",    0);
                mat.renderQueue = 3000;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            return mat;
        }
    }
}
