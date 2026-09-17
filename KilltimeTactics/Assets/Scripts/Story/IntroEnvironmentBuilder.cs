using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Scenes
{
    public static class IntroEnvironmentBuilder
    {
        private static Material _matCommandDeck;
        private static Material _matAsteroidRock;
        private static Material _matHoloBlue;
        private static Material _matStasisGlass;
        private static Material _matShipHull;
        private static Material _matShipThruster;
        private static Material _matBurningPlanet;

        public static GameObject BuildAsteroidBase(Transform parent, TacticalHexGrid grid)
        {
            InitializeMaterials();

            var root = new GameObject("[Env] AsteroidBase_R44");
            root.transform.SetParent(parent, false);

            float r = grid != null ? grid.HexRadius : 1.0f;

            BuildCommandDeck(root, r);
            BuildViewportWithNefris(root);
            BuildMedicalBay(root, r);
            BuildHangarBayAndShip(root, r);
            BuildLighting(root);

            return root;
        }

        private static void InitializeMaterials()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Killtime/TacticalLit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Diffuse")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                              ?? Shader.Find("Unlit/Color")
                              ?? Shader.Find("Sprites/Default")
                              ?? Shader.Find("Universal Render Pipeline/Lit")
                              ?? Shader.Find("Standard");

            _matCommandDeck = new Material(litShader)
            {
                name = "Mat_CommandDeck_Steel"
            };
            SetColor(_matCommandDeck, new Color(0.12f, 0.14f, 0.18f));
            if (_matCommandDeck.HasProperty("_Metallic")) _matCommandDeck.SetFloat("_Metallic", 0.75f);
            if (_matCommandDeck.HasProperty("_Smoothness")) _matCommandDeck.SetFloat("_Smoothness", 0.55f);

            _matAsteroidRock = new Material(litShader)
            {
                name = "Mat_Asteroid_Basalt"
            };
            SetColor(_matAsteroidRock, new Color(0.08f, 0.09f, 0.11f));
            if (_matAsteroidRock.HasProperty("_Smoothness")) _matAsteroidRock.SetFloat("_Smoothness", 0.1f);

            _matHoloBlue = new Material(unlitShader)
            {
                name = "Mat_HoloProjection_Blue"
            };
            Color holoCol = new Color(0.0f, 0.85f, 1.0f, 0.45f);
            SetColor(_matHoloBlue, holoCol);
            ConfigureTransparent(_matHoloBlue);

            _matStasisGlass = new Material(unlitShader)
            {
                name = "Mat_StasisGlass_Cyan"
            };
            Color glassCol = new Color(0.1f, 0.95f, 0.8f, 0.25f);
            SetColor(_matStasisGlass, glassCol);
            ConfigureTransparent(_matStasisGlass);

            _matShipHull = new Material(litShader)
            {
                name = "Mat_ShipHull_Composite"
            };
            SetColor(_matShipHull, new Color(0.22f, 0.25f, 0.30f));
            if (_matShipHull.HasProperty("_Metallic")) _matShipHull.SetFloat("_Metallic", 0.85f);
            if (_matShipHull.HasProperty("_Smoothness")) _matShipHull.SetFloat("_Smoothness", 0.65f);

            _matShipThruster = new Material(unlitShader)
            {
                name = "Mat_ShipThruster_Glow"
            };
            Color thrusterCol = new Color(0.0f, 0.9f, 1.0f);
            SetColor(_matShipThruster, thrusterCol);
            if (_matShipThruster.HasProperty("_EmissionColor"))
            {
                _matShipThruster.SetColor("_EmissionColor", thrusterCol * 2.5f);
                _matShipThruster.EnableKeyword("_EMISSION");
            }

            _matBurningPlanet = new Material(unlitShader)
            {
                name = "Mat_Nefris_Burning"
            };
            Color planetCol = new Color(1.0f, 0.32f, 0.08f);
            SetColor(_matBurningPlanet, planetCol);
            if (_matBurningPlanet.HasProperty("_EmissionColor"))
            {
                _matBurningPlanet.SetColor("_EmissionColor", planetCol * 2.0f);
                _matBurningPlanet.EnableKeyword("_EMISSION");
            }
        }

        private static void BuildCommandDeck(GameObject root, float r)
        {
            var deck = new GameObject("CommandDeck_Area");
            deck.transform.SetParent(root.transform, false);

            var table = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            table.name = "HoloTable_Center";
            table.transform.SetParent(deck.transform, false);
            table.transform.position = new Vector3(0, 0.45f, 0);
            table.transform.localScale = new Vector3(2.2f * r, 0.45f, 2.2f * r);
            table.GetComponent<Renderer>().sharedMaterial = _matCommandDeck;
            StripCollider(table);

            var holoProjection = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            holoProjection.name = "Holo_Beam";
            holoProjection.transform.SetParent(table.transform, false);
            holoProjection.transform.localPosition = new Vector3(0, 1.1f, 0);
            holoProjection.transform.localScale = new Vector3(0.85f, 1.0f, 0.85f);
            holoProjection.GetComponent<Renderer>().sharedMaterial = _matHoloBlue;
            StripCollider(holoProjection);

            var holoPlanet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            holoPlanet.name = "Holo_VardisMesh";
            holoPlanet.transform.SetParent(holoProjection.transform, false);
            holoPlanet.transform.localPosition = new Vector3(0, 0.25f, 0);
            holoPlanet.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            holoPlanet.GetComponent<Renderer>().sharedMaterial = _matHoloBlue;
            StripCollider(holoPlanet);

            BuildConsoleRow(deck, new Vector3(2.5f * r, 0.4f, 2.0f * r), 45f);
            BuildConsoleRow(deck, new Vector3(-2.5f * r, 0.4f, 2.0f * r), -45f);
        }

        private static void BuildViewportWithNefris(GameObject root)
        {
            var vp = new GameObject("Viewport_NefrisOrbit");
            vp.transform.SetParent(root.transform, false);
            vp.transform.position = new Vector3(0, 3.2f, 14f);

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Viewport_Frame";
            frame.transform.SetParent(vp.transform, false);
            frame.transform.localScale = new Vector3(18f, 5.5f, 0.4f);
            frame.GetComponent<Renderer>().sharedMaterial = _matAsteroidRock;
            StripCollider(frame);

            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.name = "Viewport_Glass";
            glass.transform.SetParent(vp.transform, false);
            glass.transform.localScale = new Vector3(16f, 4.5f, 0.1f);
            glass.GetComponent<Renderer>().sharedMaterial = _matStasisGlass;
            StripCollider(glass);

            var nefris = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nefris.name = "Planet_Nefris_BurningInVoid";
            nefris.transform.SetParent(vp.transform, false);
            nefris.transform.localPosition = new Vector3(8f, -1.5f, 45f);
            nefris.transform.localScale = new Vector3(14f, 14f, 14f);
            nefris.GetComponent<Renderer>().sharedMaterial = _matBurningPlanet;
            StripCollider(nefris);

            var debrisBelt = new GameObject("Debris_Field");
            debrisBelt.transform.SetParent(vp.transform, false);
            for (int i = 0; i < 25; i++)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = $"Debris_{i}";
                rock.transform.SetParent(debrisBelt.transform, false);
                float x = Random.Range(-25f, 25f);
                float y = Random.Range(-10f, 15f);
                float z = Random.Range(20f, 60f);
                rock.transform.localPosition = new Vector3(x, y, z);
                rock.transform.rotation = Random.rotation;
                rock.transform.localScale = Vector3.one * Random.Range(0.6f, 2.8f);
                rock.GetComponent<Renderer>().sharedMaterial = _matAsteroidRock;
                StripCollider(rock);
            }
        }

        private static void BuildMedicalBay(GameObject root, float r)
        {
            var med = new GameObject("MedicalAirlock_Area");
            med.transform.SetParent(root.transform, false);
            med.transform.position = new Vector3(-6.5f * r, 0, 0);

            var doorL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorL.name = "Medical_BlastDoor_Left";
            doorL.transform.SetParent(med.transform, false);
            doorL.transform.localPosition = new Vector3(1.8f, 1.8f, 0);
            doorL.transform.localScale = new Vector3(0.4f, 3.6f, 1.6f);
            doorL.GetComponent<Renderer>().sharedMaterial = _matCommandDeck;

            var doorR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorR.name = "Medical_BlastDoor_Right";
            doorR.transform.SetParent(med.transform, false);
            doorR.transform.localPosition = new Vector3(1.8f, 1.8f, 1.6f);
            doorR.transform.localScale = new Vector3(0.4f, 3.6f, 1.6f);
            doorR.GetComponent<Renderer>().sharedMaterial = _matCommandDeck;

            var gurney = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gurney.name = "StasisGurney_Cart";
            gurney.transform.SetParent(med.transform, false);
            gurney.transform.localPosition = new Vector3(-1.0f, 0.45f, 0.8f);
            gurney.transform.localScale = new Vector3(1.1f, 0.6f, 2.2f);
            gurney.GetComponent<Renderer>().sharedMaterial = _matCommandDeck;
            StripCollider(gurney);

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            capsule.name = "Stasis_FieldCapsule";
            capsule.transform.SetParent(gurney.transform, false);
            capsule.transform.localPosition = new Vector3(0, 0.7f, 0);
            capsule.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            capsule.transform.localScale = new Vector3(0.85f, 0.95f, 0.85f);
            capsule.GetComponent<Renderer>().sharedMaterial = _matStasisGlass;
            StripCollider(capsule);
        }

        private static void BuildHangarBayAndShip(GameObject root, float r)
        {
            var hangar = new GameObject("HangarBay_Area");
            hangar.transform.SetParent(root.transform, false);
            hangar.transform.position = new Vector3(7.5f * r, 0, -1.0f);

            var ship = new GameObject("Starlight_Voyager_Vessel");
            ship.transform.SetParent(hangar.transform, false);
            ship.transform.localPosition = new Vector3(1.5f, 0, 0);

            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Voyager_MainFuselage";
            hull.transform.SetParent(ship.transform, false);
            hull.transform.localPosition = new Vector3(0, 1.8f, 0);
            hull.transform.localScale = new Vector3(4.2f, 2.2f, 11.5f);
            hull.GetComponent<Renderer>().sharedMaterial = _matShipHull;
            StripCollider(hull);

            var cockpit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cockpit.name = "Voyager_CockpitNose";
            cockpit.transform.SetParent(ship.transform, false);
            cockpit.transform.localPosition = new Vector3(0, 2.2f, 5.2f);
            cockpit.transform.localRotation = Quaternion.Euler(-18f, 0, 0);
            cockpit.transform.localScale = new Vector3(2.8f, 1.4f, 3.2f);
            cockpit.GetComponent<Renderer>().sharedMaterial = _matStasisGlass;
            StripCollider(cockpit);

            var wingL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wingL.name = "Wing_Left";
            wingL.transform.SetParent(ship.transform, false);
            wingL.transform.localPosition = new Vector3(-3.4f, 1.4f, -1.8f);
            wingL.transform.localRotation = Quaternion.Euler(0, 25f, 10f);
            wingL.transform.localScale = new Vector3(3.2f, 0.25f, 4.5f);
            wingL.GetComponent<Renderer>().sharedMaterial = _matShipHull;
            StripCollider(wingL);

            var wingR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wingR.name = "Wing_Right";
            wingR.transform.SetParent(ship.transform, false);
            wingR.transform.localPosition = new Vector3(3.4f, 1.4f, -1.8f);
            wingR.transform.localRotation = Quaternion.Euler(0, -25f, -10f);
            wingR.transform.localScale = new Vector3(3.2f, 0.25f, 4.5f);
            wingR.GetComponent<Renderer>().sharedMaterial = _matShipHull;
            StripCollider(wingR);

            var thrusterL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            thrusterL.name = "Thruster_Left";
            thrusterL.transform.SetParent(ship.transform, false);
            thrusterL.transform.localPosition = new Vector3(-1.4f, 1.8f, -6.0f);
            thrusterL.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            thrusterL.transform.localScale = new Vector3(1.1f, 0.8f, 1.1f);
            thrusterL.GetComponent<Renderer>().sharedMaterial = _matShipThruster;
            StripCollider(thrusterL);

            var thrusterR = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            thrusterR.name = "Thruster_Right";
            thrusterR.transform.SetParent(ship.transform, false);
            thrusterR.transform.localPosition = new Vector3(1.4f, 1.8f, -6.0f);
            thrusterR.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            thrusterR.transform.localScale = new Vector3(1.1f, 0.8f, 1.1f);
            thrusterR.GetComponent<Renderer>().sharedMaterial = _matShipThruster;
            StripCollider(thrusterR);

            for (int c = 0; c < 4; c++)
            {
                var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.name = $"CargoCrate_{c}";
                crate.transform.SetParent(hangar.transform, false);
                crate.transform.localPosition = new Vector3(-1.8f + (c * 0.9f), 0.4f, -4.5f + (c % 2 * 0.8f));
                crate.transform.localScale = Vector3.one * 0.8f;
                crate.GetComponent<Renderer>().sharedMaterial = _matCommandDeck;
            }
        }

        private static void BuildConsoleRow(GameObject parent, Vector3 pos, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Console_Terminal";
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = new Vector3(1.8f, 0.9f, 0.7f);
            go.GetComponent<Renderer>().sharedMaterial = _matCommandDeck;

            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Screen_Display";
            screen.transform.SetParent(go.transform, false);
            screen.transform.localPosition = new Vector3(0, 0.5f, -0.1f);
            screen.transform.localRotation = Quaternion.Euler(-25f, 0, 0);
            screen.transform.localScale = new Vector3(1.5f, 0.45f, 0.05f);
            screen.GetComponent<Renderer>().sharedMaterial = _matHoloBlue;
            StripCollider(screen);
        }

        private static void BuildLighting(GameObject root)
        {
            var lights = new GameObject("Interior_Lighting");
            lights.transform.SetParent(root.transform, false);

            var keyLight = new GameObject("Key_Light").AddComponent<Light>();
            keyLight.transform.SetParent(lights.transform, false);
            keyLight.transform.position = new Vector3(0, 4.5f, -2f);
            keyLight.type = LightType.Directional;
            keyLight.transform.rotation = Quaternion.Euler(50f, 25f, 0);
            keyLight.color = new Color(0.85f, 0.92f, 1.0f);
            keyLight.intensity = 1.1f;

            var holoLight = new GameObject("Holo_PointLight").AddComponent<Light>();
            holoLight.transform.SetParent(lights.transform, false);
            holoLight.transform.position = new Vector3(0, 1.8f, 0);
            holoLight.type = LightType.Point;
            holoLight.range = 8.0f;
            holoLight.intensity = 3.2f;
            holoLight.color = new Color(0.0f, 0.85f, 1.0f);

            var stasisLight = new GameObject("Stasis_PointLight").AddComponent<Light>();
            stasisLight.transform.SetParent(lights.transform, false);
            stasisLight.transform.position = new Vector3(-6.5f, 1.5f, 0.8f);
            stasisLight.type = LightType.Point;
            stasisLight.range = 5.5f;
            stasisLight.intensity = 2.4f;
            stasisLight.color = new Color(0.1f, 0.95f, 0.7f);
        }

        private static void StripCollider(GameObject go)
        {
            if (go == null) return;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static void ConfigureTransparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1.0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)RenderQueue.Transparent + 10;
        }
    }
}