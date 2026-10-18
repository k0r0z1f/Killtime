using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Data
{
    public enum ScenePrimitiveKind
    {
        Sphere = 0,
        Cube = 1,
        Cylinder = 2,
        Capsule = 3,
        Quad = 4
    }

    [Serializable]
    public class SceneLightingData
    {
        public bool OverrideLighting = false;
        public Vector3 SunPosition = new Vector3(0f, 25f, 0f);
        public Vector3 SunEulerAngles = new Vector3(25f, 315f, 0f);
        public Color SunColor = new Color(1.0f, 0.92f, 0.85f);
        public float SunIntensity = 1.8f;
        public LightShadows Shadows = LightShadows.Soft;
        public float ShadowStrength = 0.85f;
        public Color AmbientColor = new Color(0.28f, 0.30f, 0.35f);
    }

    [Serializable]
    public class ScenePlaceholderData
    {
        public string Id = "placeholder_1";
        public ScenePrimitiveKind Primitive = ScenePrimitiveKind.Sphere;
        public Vector3 Position = Vector3.zero;
        public Vector3 EulerAngles = Vector3.zero;
        public Vector3 Scale = Vector3.one;
        public Color Color = Color.white;
        public float Smoothness = 0.2f;
        public bool IsEmissive = false;
        public Color EmissionColor = Color.black;
        public bool HasLight = false;
        public LightType LightType = LightType.Point;
        public Color LightColor = Color.white;
        public float LightIntensity = 8f;
        public float LightRange = 15f;
        // Enfant du décor : instancié sous l'instance du préfab d'environnement,
        // Position/EulerAngles/Scale étant alors exprimés en LOCAL (alignement hérité).
        // À false (défaut, compatible avec les anciens JSON) : coordonnées monde.
        public bool ParentToEnvironment = false;
    }

    [Serializable]
    public class SceneCinematicData
    {
        public string CinematicId = "";
        public string Title = "Nouvelle cinématique";
        public bool Skippable = true;
        public float PlaybackSpeed = 1f;
        public bool Letterbox = true;
        public bool HideSceneChat = false;
        public bool InPlace = false;
        public CinematicCameraTransition StartTransition = CinematicCameraTransition.Teleport;
        public float StartTransitionDuration = 0.5f;
        public CinematicCameraTransition EndTransition = CinematicCameraTransition.Teleport;
        public float EndTransitionDuration = 0.5f;
        public string NextTargetId = "";
        public List<SceneCinematicShotData> Shots = new();
        public float GraphPosX = 60f;
        public float GraphPosY = 1950f;
        public float CardWidth = 360f;
        public float CardHeight = 190f;

        public string GetSummary()
        {
            int n = Shots != null ? Shots.Count : 0;
            float total = 0f;
            if (Shots != null)
                for (int i = 0; i < Shots.Count; i++)
                    if (Shots[i] != null) total += Mathf.Max(0.1f, Shots[i].Duration + Shots[i].StartDelay);
            if (PlaybackSpeed > 0.01f) total /= PlaybackSpeed;
            string inPlaceTag = InPlace ? " [sur place]" : "";
            return n == 0 ? "(aucun plan)" : $"{n} plan(s) · ~{total:0.0}s{inPlaceTag}";
        }
    }

}