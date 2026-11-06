// VIRTUAL LOFT — LoftBody.cs: moves the skeleton on top of the Animator: a walk matched
// to real ground speed, and quick touches for the ball game. All animals share the same bone
// names, so this serves every skin. Rotations use the agent's own axes, not the bones'.

using UnityEngine;

public class LoftBody : MonoBehaviour
{
    public enum Touch { None, Foot, Knee, Head }

    const float STRIDE = 0.42f;      // meters per half step
    Transform thighL, thighR, footL, footR, armL, armR, foreL, foreR, head, chest, mesh;
    Vector3 lastPos, meshBase;
    float phase, walk, touchT = 1f;
    const float TOUCH_TIME = 0.42f;
    Touch touch;
    bool left;
    public bool HasLegs => thighL != null && thighR != null;

    public void Bind(Transform model)
    {
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
            switch (t.name)
            {
                case "thigh.L": thighL = t; break;
                case "thigh.R": thighR = t; break;
                case "foot.L": footL = t; break;
                case "foot.R": footR = t; break;
                case "upperarm.L": armL = t; break;
                case "upperarm.R": armR = t; break;
                case "forearm.L": foreL = t; break;
                case "forearm.R": foreR = t; break;
                case "head": head = t; break;
                case "chest": chest = t; break;
                case "Mesh": mesh = t; break;
            }
        if (mesh != null) meshBase = mesh.localPosition;
        lastPos = transform.position;
    }

    public void Play(Touch kind, bool leftSide)
    {
        touch = kind; left = leftSide; touchT = 0f;
    }

    void Swing(Transform bone, float degrees)
    {
        if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, transform.right) * bone.rotation;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        var step = transform.position - lastPos; step.y = 0f;
        lastPos = transform.position;
        float speed = dt > 0f ? step.magnitude / dt : 0f;
        walk = Mathf.MoveTowards(walk, speed > 0.12f && speed < 6f ? 1f : 0f, dt * 5f);
        phase += step.magnitude / STRIDE * Mathf.PI;
        float lift = 0f;

        if (walk > 0.001f && HasLegs)
        {
            float s = Mathf.Sin(phase), amp = 30f * walk;
            Swing(thighL, -s * amp);
            Swing(thighR, s * amp);
            // the leg going forward bends so the foot clears the floor
            Swing(footL, Mathf.Max(0f, Mathf.Cos(phase)) * 42f * walk);
            Swing(footR, Mathf.Max(0f, -Mathf.Cos(phase)) * 42f * walk);
            Swing(armL, s * 20f * walk);
            Swing(armR, -s * 20f * walk);
            Swing(chest, 4f * walk);
            lift = Mathf.Abs(Mathf.Cos(phase)) * 0.035f * walk;
        }

        if (touchT < 1f)
        {
            touchT += dt / TOUCH_TIME;
            float k = Mathf.Sin(Mathf.Clamp01(touchT) * Mathf.PI);
            var thigh = left ? thighL : thighR;
            var foot = left ? footL : footR;
            switch (touch)
            {
                case Touch.Foot:
                    Swing(thigh, -58f * k);
                    Swing(foot, -18f * k);
                    Swing(armL, -24f * k); Swing(armR, -24f * k);
                    break;
                case Touch.Knee:
                    Swing(thigh, -82f * k);
                    Swing(foot, 72f * k);
                    Swing(armL, 22f * k); Swing(armR, 22f * k);
                    lift += 0.05f * k;
                    break;
                case Touch.Head:
                    Swing(head, -24f * Mathf.Sin(Mathf.Clamp01(touchT) * Mathf.PI * 2f));
                    Swing(armL, -36f * k); Swing(armR, -36f * k);
                    lift += 0.13f * k;
                    break;
            }
        }
        if (mesh != null) mesh.localPosition = meshBase + Vector3.up * lift;
    }
}
