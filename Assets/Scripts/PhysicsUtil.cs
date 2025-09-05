using UnityEngine;

public static class PhysicsUtil {
    /// <summary>
    /// CapsuleCollider를 CapsuleCast 등에 활용할 수 있는
    /// 월드 좌표계 p0, p1, radius로 변환
    /// </summary>
    /// <param name="capsule"></param>
    /// <param name="p0"></param>
    /// <param name="p1"></param>
    /// <param name="radius"></param>
    /// <returns></returns>
    public static bool TryGetCapsuleWorld(this CapsuleCollider capsule, out Vector3 p0, out Vector3 p1, out float radius) {
        p0 = p1 = default; radius = default;

        if (capsule == null || !capsule.enabled) {
            return false;
        }

        Transform tr = capsule.transform;

        // 캡슐 로컬센터 -> 월드
        Vector3 c = tr.TransformPoint(capsule.center);

        // 방향축/스케일 추출
        Vector3 axis;
        float axisScale, rScaleA, rScaleB;
        Vector3 ls = tr.lossyScale;
        switch (capsule.direction) {
            case 0: // X
                axis = tr.right;
                axisScale = Mathf.Abs(ls.x);
                rScaleA = Mathf.Abs(ls.y);
                rScaleB = Mathf.Abs(ls.z);
                break;
            case 2: // Z
                axis = tr.forward;
                axisScale = Mathf.Abs(ls.z);
                rScaleA = Mathf.Abs(ls.x);
                rScaleB = Mathf.Abs(ls.y);
                break;
            default: // Y
                axis = tr.up;
                axisScale = Mathf.Abs(ls.y);
                rScaleA = Mathf.Abs(ls.x);
                rScaleB = Mathf.Abs(ls.z);
                break;
        }

        radius = capsule.radius * Mathf.Max(rScaleA, rScaleB);
        float heightScaled = Mathf.Max(capsule.height * axisScale, radius * 2f);

        float half = (heightScaled * 0.5f) - radius;
        p0 = c + axis * half;
        p1 = c - axis * half;
        return true;
    }

    /// <summary>
    /// BoxCollider를 BoxCast 등에 활용할 수 있는
    /// 월드 좌표계 center, halfExtents, orientation으로 변환
    /// </summary>
    /// <param name="box"></param>
    /// <param name="center"></param>
    /// <param name="halfExtents"></param>
    /// <param name="orientation"></param>
    /// <returns></returns>
    public static bool TryGetBoxWorld(this BoxCollider box, out Vector3 center, out Vector3 halfExtents, out Quaternion orientation) {
        center = default; halfExtents = default; orientation = default;

        if (box == null || !box.enabled) {
            return false;
        }

        Transform tr = box.transform;
        Vector3 ls = tr.lossyScale;

        halfExtents = 0.5f * new Vector3(
            box.size.x * Mathf.Abs(ls.x),
            box.size.y * Mathf.Abs(ls.y),
            box.size.z * Mathf.Abs(ls.z)
        );

        orientation = tr.rotation;

        center = tr.TransformPoint(box.center);
        return true;
    }
}