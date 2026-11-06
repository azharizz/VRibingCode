// VIRTUAL LOFT — the "life" layer: a handful of cheap CPU animations (drifting
// clouds, orbiting birds, shore foam, streaking planes/comets, spinning sky).
// Wind sway, water, blinking windows and traffic run in shaders instead.
using System;
using System.Collections.Generic;
using UnityEngine;

public class WorldLife : MonoBehaviour
{
    readonly List<Action<float, float>> tasks = new List<Action<float, float>>();

    public void Add(Action<float, float> task) => tasks.Add(task); // (time, dt)

    // Drift along `vel`, wrapping inside [-range, range] on the dominant axis.
    public void Drift(Transform t, Vector3 vel, float range)
    {
        var axis = Mathf.Abs(vel.x) >= Mathf.Abs(vel.z) ? 0 : 2;
        Add((time, dt) =>
        {
            if (t == null) return;
            var p = t.localPosition + vel * dt;
            if (axis == 0 && Mathf.Abs(p.x) > range) p.x = -Mathf.Sign(p.x) * range;
            if (axis == 2 && Mathf.Abs(p.z) > range) p.z = -Mathf.Sign(p.z) * range;
            t.localPosition = p;
        });
    }

    public void Spin(Transform t, Vector3 degPerSec) => Add((time, dt) => { if (t != null) t.Rotate(degPerSec * dt, Space.Self); });

    // Circle a point at radius/height; flap by scaling Y (wings are a flat V).
    public void Bird(Transform t, Vector3 center, float radius, float speed, float phase, float flapHz)
    {
        Add((time, dt) =>
        {
            if (t == null) return;
            float a = time * speed + phase;
            var p = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a * 1.7f + phase) * 0.8f, Mathf.Sin(a) * radius * 0.6f);
            var v = new Vector3(-Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius * 0.6f);
            t.localPosition = p;
            if (v.sqrMagnitude > 1e-4f) t.localRotation = Quaternion.LookRotation(v, Vector3.up);
            t.localScale = new Vector3(1f, Mathf.Sin(time * flapHz * 6.28f + phase * 3f) * 0.7f + 0.2f, 1f);
        });
    }

    public void Tick(float t, float dt) { for (int i = 0; i < tasks.Count; i++) tasks[i](t, dt); }
    void Update() => Tick(Time.time, Time.deltaTime);
}
