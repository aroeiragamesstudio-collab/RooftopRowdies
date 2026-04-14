using System.Collections.Generic;
using UnityEngine;

public class RopeRenderer : MonoBehaviour
{
    [Header("Character References")]
    [Tooltip("The Gun character's transform (point A of the rope).")]
    public Transform pointA;

    [Tooltip("The Jump character's transform (point B of the rope).")]
    public Transform pointB;

    [Header("Joint Reference")]
    [Tooltip("The DistanceJoint2D that connects both characters (on JumpCharacterController).")]
    public DistanceJoint2D distanceJoint;

    [Header("Rope Visual Settings")]
    [Tooltip("Bézier segments per sub-segment of rope. More = smoother curve.")]
    [Range(5, 30)]
    public int segmentsPerSpan = 10;

    [Tooltip("Multiplier that controls how exaggerated the sag looks.")]
    [Range(0.1f, 3f)]
    public float sagMultiplier = 1f;

    [Tooltip("Minimum sag so the rope never looks perfectly rigid.")]
    [Range(0f, 1f)]
    public float minimumSag = 0.15f;

    [Tooltip("How quickly the visual sag adjusts to changes.")]
    [Range(1f, 50f)]
    public float sagSmoothSpeed = 12f;

    [Header("Sway (Optional)")]
    [Tooltip("Enable a subtle side-to-side sway on the rope.")]
    public bool enableSway = true;

    [Tooltip("Amplitude of the sway oscillation in world units.")]
    [Range(0f, 0.5f)]
    public float swayAmplitude = 0.1f;

    [Tooltip("Speed of the sway oscillation.")]
    [Range(0.5f, 5f)]
    public float swayFrequency = 1.5f;

    [Header("Wall Wrapping")]
    [Tooltip("Which layers the rope should wrap around. Set this to your Wall layer (and optionally Ground).")]
    public LayerMask wrapLayer;

    [Tooltip("Small offset to push wrap points away from corners so the rope doesn't clip into the collider surface.")]
    [Range(0.01f, 0.5f)]
    public float cornerOffset = 0.1f;

    [Tooltip("Maximum number of wrap points allowed. Prevents infinite subdivision in degenerate cases.")]
    [Range(2, 20)]
    public int maxWrapPoints = 10;

    // -----------------------------------------------------------------------
    // Internal state
    // -----------------------------------------------------------------------
    LineRenderer lineRenderer;
    float currentSag;

    /// <summary>
    /// The list of intermediate wrap points where the rope bends around corners.
    /// Does NOT include pointA and pointB themselves — those are always the endpoints.
    /// The full rope path is: pointA → wrapPoints[0] → wrapPoints[1] → ... → pointB
    /// </summary>
    List<Vector2> wrapPoints = new List<Vector2>();

    /// <summary>
    /// For each wrap point, we store which Collider2D it's wrapping around.
    /// This is used during unwrapping: we only remove a wrap point if the
    /// collider is no longer obstructing line-of-sight on the adjacent segment.
    /// </summary>
    List<Collider2D> wrapColliders = new List<Collider2D>();

    // Reusable buffer for LineRenderer positions (avoids GC allocations)
    List<Vector3> renderPoints = new List<Vector3>();

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();

        if (lineRenderer == null)
        {
            Debug.LogError("[RopeRenderer] LineRenderer component not found!");
            enabled = false;
            return;
        }

        if (pointA == null || pointB == null)
        {
            Debug.LogError("[RopeRenderer] PointA and PointB must be assigned!");
            enabled = false;
            return;
        }

        if (distanceJoint == null)
        {
            Debug.LogError("[RopeRenderer] DistanceJoint2D must be assigned!");
            enabled = false;
            return;
        }

        lineRenderer.useWorldSpace = true;
        currentSag = minimumSag;
    }

    void LateUpdate()
    {
        if (pointA == null || pointB == null || distanceJoint == null) return;

        Vector2 posA = pointA.position;
        Vector2 posB = pointB.position;

        // ===================================================================
        // PHASE 1: UNWRAP — Remove wrap points that are no longer needed.
        // We do this FIRST so we don't accumulate stale points.
        // ===================================================================
        UnwrapPoints(posA, posB);

        // ===================================================================
        // PHASE 2: WRAP — Detect new wall obstructions and add wrap points.
        // ===================================================================
        WrapPoints(posA, posB);

        // ===================================================================
        // PHASE 3: RENDER — Build the visual rope with Bézier sag on each span.
        // ===================================================================
        RenderRope(posA, posB);
    }

    // =======================================================================
    // WRAPPING: Detect walls between consecutive rope points
    // =======================================================================

    /// <summary>
    /// For each straight segment of the rope path, cast a ray to check if a
    /// wall collider is in the way. If so, find the nearest corner of that
    /// collider and insert it as a new wrap point.
    ///
    /// The algorithm:
    /// 1. Build the full path (posA → wrapPoints → posB).
    /// 2. For each pair of consecutive points, do a Physics2D.Linecast.
    /// 3. If something on wrapLayer is hit, call FindBestCorner() to get
    ///    the collider corner the rope should bend around.
    /// 4. Insert that corner + a small offset into wrapPoints.
    /// 5. Restart the loop (because the path changed).
    ///
    /// The safety counter prevents infinite loops in degenerate cases
    /// (e.g. extremely complex geometry where every subdivision still hits).
    /// </summary>
    void WrapPoints(Vector2 posA, Vector2 posB)
    {
        if (wrapPoints.Count >= maxWrapPoints) return;

        bool foundNew = true;
        int safety = 0;

        while (foundNew && wrapPoints.Count < maxWrapPoints && safety < maxWrapPoints)
        {
            safety++;
            foundNew = false;

            List<Vector2> path = BuildPath(posA, posB);

            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 from = path[i];
                Vector2 to = path[i + 1];

                RaycastHit2D hit = Physics2D.Linecast(from, to, wrapLayer);

                if (hit.collider != null)
                {
                    // Collect corners that are already used on THIS collider
                    // so FindBestCorner can skip them and pick a different corner.
                    // This is what fixes the "wide wall" bug: after wrapping the
                    // top-left corner, the next segment still hits the same collider,
                    // but now FindBestCorner will exclude the top-left and pick
                    // the top-right instead.
                    List<Vector2> excludeCorners = new List<Vector2>();
                    for (int w = 0; w < wrapColliders.Count; w++)
                    {
                        if (wrapColliders[w] == hit.collider)
                            excludeCorners.Add(wrapPoints[w]);
                    }

                    Vector2 corner = FindBestCorner(hit.collider, from, to, excludeCorners);

                    // If FindBestCorner returned Vector2.zero it means all viable
                    // corners are already in use — nothing to add for this segment.
                    if (corner == Vector2.zero)
                        continue;

                    // Offset the corner slightly away from the collider center
                    // so the rope doesn't clip into the surface.
                    Vector2 colliderCenter = hit.collider.bounds.center;
                    Vector2 awayDir = (corner - colliderCenter).normalized;
                    corner += awayDir * cornerOffset;

                    if (!IsNearExistingWrapPoint(corner, 0.15f))
                    {
                        int wrapInsertIndex = Mathf.Clamp(i, 0, wrapPoints.Count);

                        wrapPoints.Insert(wrapInsertIndex, corner);
                        wrapColliders.Insert(wrapInsertIndex, hit.collider);

                        foundNew = true;
                        break; // Restart with updated path
                    }
                }
            }
        }
    }

    // =======================================================================
    // UNWRAPPING: Remove wrap points that are no longer obstructing
    // =======================================================================

    /// <summary>
    /// For each existing wrap point, check if the rope still needs to bend there.
    ///
    /// The test: raycast from the PREVIOUS point to the NEXT point (skipping
    /// the wrap point entirely). If the ray is clear, OR if it hits a different
    /// collider than the one we originally wrapped around, then this wrap point
    /// is no longer serving a purpose and gets removed.
    ///
    /// We iterate backwards so that removing an element doesn't shift the
    /// indices of elements we haven't checked yet.
    /// </summary>
    void UnwrapPoints(Vector2 posA, Vector2 posB)
    {
        for (int i = wrapPoints.Count - 1; i >= 0; i--)
        {
            Vector2 prev = (i == 0) ? posA : wrapPoints[i - 1];
            Vector2 next = (i == wrapPoints.Count - 1) ? posB : wrapPoints[i + 1];

            // If the collider was destroyed, remove immediately
            if (wrapColliders[i] == null)
            {
                wrapPoints.RemoveAt(i);
                wrapColliders.RemoveAt(i);
                continue;
            }

            RaycastHit2D hit = Physics2D.Linecast(prev, next, wrapLayer);

            if (hit.collider == null || hit.collider != wrapColliders[i])
            {
                wrapPoints.RemoveAt(i);
                wrapColliders.RemoveAt(i);
            }
        }
    }

    // =======================================================================
    // RENDERING: Build the visual rope with Bézier sag per span
    // =======================================================================

    /// <summary>
    /// For each sub-segment of the rope (between consecutive points in the
    /// full path), apply the Bézier sag independently. This means:
    ///
    /// - If there are NO wrap points: one Bézier curve from A to B (original behavior).
    /// - If there ARE wrap points: each sub-segment gets its own proportional sag.
    ///   Shorter segments get less sag because there's less "rope" in that span.
    ///
    /// The sag for each span = totalSag × (spanLength / totalPathLength).
    /// Very short spans (< 0.5 units, near wrap points) get minimal sag
    /// to keep the corners looking crisp.
    /// </summary>
    void RenderRope(Vector2 posA, Vector2 posB)
    {
        List<Vector2> path = BuildPath(posA, posB);

        // Calculate total rope length and actual path length
        float ropeLength = distanceJoint.distance;
        float totalPathLength = 0f;
        for (int i = 0; i < path.Count - 1; i++)
            totalPathLength += Vector2.Distance(path[i], path[i + 1]);

        float totalSlack = Mathf.Max(0f, ropeLength - totalPathLength);

        // Smooth the sag
        float targetSag = (totalSlack * sagMultiplier) + minimumSag;
        currentSag = Mathf.Lerp(currentSag, targetSag, Time.deltaTime * sagSmoothSpeed);

        // Build the render points
        renderPoints.Clear();

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 from = path[i];
            Vector2 to = path[i + 1];
            float spanLength = Vector2.Distance(from, to);

            // Proportional sag for this span
            float spanRatio = (totalPathLength > 0.01f) ? (spanLength / totalPathLength) : 1f;
            float spanSag = currentSag * spanRatio;

            // Minimal sag for very short spans near wrap corners
            if (spanLength < 0.5f)
                spanSag = Mathf.Min(spanSag, 0.05f);

            // Bézier control point: midpoint shifted down by sag
            Vector2 midpoint = (from + to) * 0.5f;
            Vector2 controlPoint = midpoint + Vector2.down * spanSag;

            // Optional sway (only on longer spans with slack)
            if (enableSway && swayAmplitude > 0f && spanLength > 1f)
            {
                Vector2 spanDir = (to - from).normalized;
                Vector2 perp = new Vector2(-spanDir.y, spanDir.x);
                float slackRatio = (ropeLength > 0.01f) ? (totalSlack / ropeLength) : 0f;
                // Offset phase per span so adjacent spans don't sway in sync
                float sway = Mathf.Sin(Time.time * swayFrequency + i * 1.3f) * swayAmplitude;
                sway *= Mathf.Clamp01(slackRatio * 2f);
                controlPoint += perp * sway;
            }

            // Sample the Bézier curve
            // Skip the last point of non-final spans to avoid duplicate junction points
            int endIndex = (i < path.Count - 2) ? segmentsPerSpan - 1 : segmentsPerSpan;

            for (int j = 0; j <= endIndex; j++)
            {
                float t = (float)j / segmentsPerSpan;
                renderPoints.Add(QuadraticBezier(from, controlPoint, to, t));
            }
        }

        // Apply to LineRenderer
        lineRenderer.positionCount = renderPoints.Count;
        for (int i = 0; i < renderPoints.Count; i++)
            lineRenderer.SetPosition(i, renderPoints[i]);
    }

    // =======================================================================
    // CORNER FINDING: Determine which corner of a collider to wrap around
    // =======================================================================

    /// <summary>
    /// Given a collider that's obstructing a rope segment (from → to),
    /// find the best corner to wrap around.
    ///
    /// The excludeCorners list contains wrap points already placed on this
    /// same collider (from previous iterations). This is critical for wide
    /// walls: after wrapping the top-left corner, the next segment still
    /// hits the same collider, but excludeCorners ensures we pick the
    /// top-right corner instead of the same top-left again.
    ///
    /// Returns Vector2.zero if all viable corners are already excluded.
    /// </summary>
    Vector2 FindBestCorner(Collider2D collider, Vector2 from, Vector2 to, List<Vector2> excludeCorners)
    {
        if (collider is BoxCollider2D box)
            return FindBoxCorner(box, from, to, excludeCorners);
        else if (collider is PolygonCollider2D poly)
            return FindPolygonCorner(poly, from, to, excludeCorners);
        else
        {
            Vector2 mid = (from + to) * 0.5f;
            return collider.ClosestPoint(mid);
        }
    }

    /// <summary>
    /// Checks if a world-space corner is near any point in the exclusion list.
    /// Uses a tolerance that accounts for the cornerOffset that was applied
    /// when the wrap point was originally stored.
    /// </summary>
    bool IsExcluded(Vector2 corner, List<Vector2> excludeCorners)
    {
        // The tolerance needs to be generous because the stored wrap points
        // have cornerOffset applied, so they won't be at the exact raw corner.
        float tolerance = cornerOffset + 0.2f;
        for (int i = 0; i < excludeCorners.Count; i++)
        {
            if (Vector2.Distance(corner, excludeCorners[i]) < tolerance)
                return true;
        }
        return false;
    }

    /// <summary>
    /// For a BoxCollider2D, calculate all 4 corners in world space,
    /// then pick the best one for the rope to wrap around.
    ///
    /// Instead of using a cross-product side check (which breaks when characters
    /// swap sides and the line direction flips), we use a path-validation approach:
    ///
    /// For each candidate corner (with offset applied), we test whether the two
    /// new rope segments (from→corner and corner→to) would be CLEAR of this
    /// collider. If both segments are clear, this corner creates a valid detour.
    /// Among valid detours, we pick the one with the shortest total path length.
    ///
    /// This is direction-independent: it doesn't matter which character is on
    /// which side. The rope always finds the shortest clear path around the wall.
    /// </summary>
    Vector2 FindBoxCorner(BoxCollider2D box, Vector2 from, Vector2 to, List<Vector2> excludeCorners)
    {
        Vector2 halfSize = box.size * 0.5f;
        Vector2 offset = box.offset;
        Vector2[] localCorners = new Vector2[]
        {
            offset + new Vector2(-halfSize.x, -halfSize.y), // bottom-left
            offset + new Vector2( halfSize.x, -halfSize.y), // bottom-right
            offset + new Vector2( halfSize.x,  halfSize.y), // top-right
            offset + new Vector2(-halfSize.x,  halfSize.y), // top-left
        };

        Transform t = box.transform;
        Vector2[] worldCorners = new Vector2[4];
        for (int i = 0; i < 4; i++)
            worldCorners[i] = t.TransformPoint(localCorners[i]);

        Vector2 colliderCenter = box.bounds.center;

        Vector2 bestCorner = Vector2.zero;
        float bestPathLength = float.MaxValue;
        bool foundAny = false;

        for (int i = 0; i < worldCorners.Length; i++)
        {
            if (IsExcluded(worldCorners[i], excludeCorners))
                continue;

            // Apply the offset to get the candidate wrap position
            Vector2 awayDir = (worldCorners[i] - colliderCenter).normalized;
            Vector2 candidate = worldCorners[i] + awayDir * cornerOffset;

            // Test if BOTH new segments (from→candidate and candidate→to)
            // are clear of THIS specific collider.
            // We use RaycastAll and check if the specific box is in the results,
            // because Linecast returns the first hit which might be a different collider.
            bool segmentAClear = !DoesSegmentHitCollider(from, candidate, box);
            bool segmentBClear = !DoesSegmentHitCollider(candidate, to, box);

            if (segmentAClear && segmentBClear)
            {
                // Both segments are clear — this is a valid detour.
                // Score by total path length (shorter = better).
                float pathLength = Vector2.Distance(from, candidate) + Vector2.Distance(candidate, to);

                if (pathLength < bestPathLength)
                {
                    bestPathLength = pathLength;
                    bestCorner = worldCorners[i]; // Return raw corner; offset is applied by caller
                    foundAny = true;
                }
            }
        }

        // Fallback: if no corner produced a fully clear path (can happen with
        // very tight geometry), fall back to the closest corner above the line
        // midpoint to avoid going underground.
        if (!foundAny)
        {
            float midY = (from.y + to.y) * 0.5f;
            float bestDist = float.MaxValue;

            for (int i = 0; i < worldCorners.Length; i++)
            {
                if (IsExcluded(worldCorners[i], excludeCorners))
                    continue;

                // Prefer corners above the midpoint of from→to
                if (worldCorners[i].y < midY)
                    continue;

                float dist = DistanceToLineSegment(worldCorners[i], from, to);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestCorner = worldCorners[i];
                    foundAny = true;
                }
            }
        }

        return foundAny ? bestCorner : Vector2.zero;
    }

    /// <summary>
    /// Checks whether a line segment from A to B passes through a specific collider.
    /// Uses RaycastAll so we can check for a particular collider even if it's not
    /// the first thing hit along the ray.
    /// </summary>
    bool DoesSegmentHitCollider(Vector2 a, Vector2 b, Collider2D target)
    {
        Vector2 dir = b - a;
        float distance = dir.magnitude;

        if (distance < 0.01f) return false;

        RaycastHit2D[] hits = Physics2D.RaycastAll(a, dir.normalized, distance, wrapLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == target)
                return true;
        }
        return false;
    }

    /// <summary>
    /// For PolygonCollider2D, iterate through all vertices of the polygon
    /// and pick the best one using the same cross-product logic as box corners.
    /// This handles irregular wall shapes, slopes, and complex geometry.
    ///
    /// PolygonCollider2D can have multiple paths (for shapes with holes),
    /// so we iterate all paths.
    /// </summary>
    Vector2 FindPolygonCorner(PolygonCollider2D poly, Vector2 from, Vector2 to, List<Vector2> excludeCorners)
    {
        Transform t = poly.transform;
        Vector2 colliderCenter = poly.bounds.center;

        Vector2 bestCorner = Vector2.zero;
        float bestPathLength = float.MaxValue;
        bool foundAny = false;

        for (int pathIdx = 0; pathIdx < poly.pathCount; pathIdx++)
        {
            Vector2[] points = poly.GetPath(pathIdx);

            for (int i = 0; i < points.Length; i++)
            {
                Vector2 worldPoint = t.TransformPoint(points[i] + poly.offset);

                if (IsExcluded(worldPoint, excludeCorners))
                    continue;

                Vector2 awayDir = (worldPoint - colliderCenter).normalized;
                Vector2 candidate = worldPoint + awayDir * cornerOffset;

                bool segmentAClear = !DoesSegmentHitCollider(from, candidate, poly);
                bool segmentBClear = !DoesSegmentHitCollider(candidate, to, poly);

                if (segmentAClear && segmentBClear)
                {
                    float pathLength = Vector2.Distance(from, candidate) + Vector2.Distance(candidate, to);
                    if (pathLength < bestPathLength)
                    {
                        bestPathLength = pathLength;
                        bestCorner = worldPoint;
                        foundAny = true;
                    }
                }
            }
        }

        // Fallback: prefer vertices above the midpoint
        if (!foundAny)
        {
            float midY = (from.y + to.y) * 0.5f;
            float bestDist = float.MaxValue;

            for (int pathIdx = 0; pathIdx < poly.pathCount; pathIdx++)
            {
                Vector2[] points = poly.GetPath(pathIdx);

                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 worldPoint = t.TransformPoint(points[i] + poly.offset);

                    if (IsExcluded(worldPoint, excludeCorners))
                        continue;

                    if (worldPoint.y < midY)
                        continue;

                    float dist = DistanceToLineSegment(worldPoint, from, to);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestCorner = worldPoint;
                        foundAny = true;
                    }
                }
            }
        }

        return foundAny ? bestCorner : Vector2.zero;
    }

    // =======================================================================
    // UTILITY METHODS
    // =======================================================================

    /// <summary>
    /// Build the full ordered path: posA → wrapPoints[0..N] → posB
    /// </summary>
    List<Vector2> BuildPath(Vector2 posA, Vector2 posB)
    {
        List<Vector2> path = new List<Vector2>(wrapPoints.Count + 2);
        path.Add(posA);
        path.AddRange(wrapPoints);
        path.Add(posB);
        return path;
    }

    /// <summary>
    /// Checks if a candidate position is within 'tolerance' of any existing wrap point.
    /// </summary>
    bool IsNearExistingWrapPoint(Vector2 candidate, float tolerance)
    {
        for (int i = 0; i < wrapPoints.Count; i++)
        {
            if (Vector2.Distance(wrapPoints[i], candidate) < tolerance)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 2D cross product (returns scalar).
    /// Positive = B is counter-clockwise from A.
    /// Negative = B is clockwise from A.
    /// Used to determine which side of a line a point is on.
    /// </summary>
    float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    /// <summary>
    /// Shortest distance from a point to a line segment.
    /// Projects the point onto the line, clamped to [0, 1].
    /// </summary>
    float DistanceToLineSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 line = to - from;
        float lengthSq = line.sqrMagnitude;

        if (lengthSq < 0.0001f)
            return Vector2.Distance(point, from);

        float t = Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSq);
        Vector2 projection = from + t * line;
        return Vector2.Distance(point, projection);
    }

    /// <summary>
    /// Evaluates a point on a quadratic Bézier curve.
    /// B(t) = (1-t)² * p0  +  2(1-t)t * p1  +  t² * p2
    /// </summary>
    Vector3 QuadraticBezier(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float oneMinusT = 1f - t;
        Vector2 point =
            (oneMinusT * oneMinusT) * p0 +
            (2f * oneMinusT * t) * p1 +
            (t * t) * p2;
        return point;
    }

    // =======================================================================
    // DEBUG GIZMOS
    // =======================================================================

    void OnDrawGizmos()
    {
        if (pointA == null || pointB == null || distanceJoint == null) return;

        Vector2 posA = pointA.position;
        Vector2 posB = pointB.position;

        // Draw wrap points as green spheres
        Gizmos.color = Color.green;
        foreach (var wp in wrapPoints)
            Gizmos.DrawWireSphere((Vector3)wp, 0.2f);

        // Draw the straight-line path through wrap points
        List<Vector2> path = BuildPath(posA, posB);
        Gizmos.color = Color.yellow;
        for (int i = 0; i < path.Count - 1; i++)
            Gizmos.DrawLine((Vector3)path[i], (Vector3)path[i + 1]);
    }
}
