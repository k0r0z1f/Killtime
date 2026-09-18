using System;
using System.Collections;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Story
{
    /// <summary>
    /// Gestionnaire et rendu visuel de la transition cinématique plein écran entre les scènes.
    /// Affiche un fondu au noir arcanotech, des bandes cinématiques (letterboxing),
    /// un carton de titre stylisé (Volume, Titre de scène, Sous-titre / Canon)
    /// et un indicateur de chargement tactique avec synchronisation sonore.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneTransitionOverlay : MonoBehaviour
    {
        public static SceneTransitionOverlay Instance { get; private set; }

        [Header("Paramètres Temporels")]
        [SerializeField] private float _fadeOutDuration = 0.65f;
        [SerializeField] private float _titleCardHoldDuration = 1.25f;
        [SerializeField] private float _fadeInDuration = 0.65f;
        [SerializeField] private float _maxLetterboxHeight = 54f;

        public bool IsTransitioning { get; private set; }
        public float OverlayAlpha { get; private set; } = 0f;

        private float _titleCardAlpha = 0f;
        private float _letterboxHeight = 0f;
        private string _cardVolume = "";
        private string _cardTitle = "";
        private string _cardCanonRef = "";
        private string _cardStatus = "";
        private bool _isCampaignEndCard = false;
        private Action _campaignEndDismissAction = null;

        private static Texture2D _whiteTex;
        private static Texture2D _gradientTex;
        private GUIStyle _titleStyle;
        private GUIStyle _volumeStyle;
        private GUIStyle _subStyle;
        private GUIStyle _statusStyle;

        public static SceneTransitionOverlay EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<SceneTransitionOverlay>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var go = new GameObject("[Story] SceneTransitionOverlay");
            Instance = go.AddComponent<SceneTransitionOverlay>();
            DontDestroyOnLoad(go);
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitTextures();
        }

        private static void InitTextures()
        {
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }

            if (_gradientTex == null)
            {
                int w = 128;
                _gradientTex = new Texture2D(w, 1, TextureFormat.RGBA32, false);
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / (w - 1);
                    float a = Mathf.Sin(u * Mathf.PI);
                    a = Mathf.Pow(a, 1.8f);
                    _gradientTex.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
                }
                _gradientTex.Apply();
            }
        }

        private void InitStyles()
        {
            if (_titleStyle != null) return;

            _volumeStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                richText = true
            };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                richText = true,
                wordWrap = true
            };

            _subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Italic,
                richText = true
            };

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                richText = true
            };
        }

        /// <summary>
        /// Exécute la séquence complète de transition entre deux scènes :
        /// 1. Fondu au noir + bandes cinématiques
        /// 2. Affichage du carton de titre de la scène entrante
        /// 3. Exécution du chargement sous le rideau
        /// 4. Maintien cinématique
        /// 5. Fondu de réouverture vers la nouvelle scène
        /// </summary>
        public IEnumerator PlaySceneTransitionRoutine(string nextVolume, string nextTitle, string nextCanonRef, Func<IEnumerator> loadSceneAction)
        {
            IsTransitioning = true;
            _isCampaignEndCard = false;
            _campaignEndDismissAction = null;

            _cardVolume = string.IsNullOrWhiteSpace(nextVolume) ? "MISSION TACTIQUE" : nextVolume.ToUpperInvariant();
            _cardTitle = string.IsNullOrWhiteSpace(nextTitle) ? "Scène Suivante" : nextTitle;
            _cardCanonRef = nextCanonRef ?? "";
            _cardStatus = "INITIALISATION DU THÉÂTRE TACTIQUE";

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Close, 0.7f);
            }

            // --- PHASE 1 : Fondu au noir & bandes cinématographiques ---
            float elapsed = 0f;
            while (elapsed < _fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeOutDuration);
                float smooth = t * t * (3f - 2f * t);
                OverlayAlpha = smooth;
                _letterboxHeight = Mathf.Lerp(0f, _maxLetterboxHeight, smooth);
                yield return null;
            }
            OverlayAlpha = 1f;
            _letterboxHeight = _maxLetterboxHeight;

            // --- PHASE 2 : Affichage du carton de titre & chargement de scène ---
            elapsed = 0f;
            const float titleFadeInTime = 0.35f;
            while (elapsed < titleFadeInTime)
            {
                elapsed += Time.unscaledDeltaTime;
                _titleCardAlpha = Mathf.Clamp01(elapsed / titleFadeInTime);
                yield return null;
            }
            _titleCardAlpha = 1f;

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayUI(SoundId.Turn_Start, 0.45f);
            }

            // Exécution du chargement de la scène
            if (loadSceneAction != null)
            {
                yield return loadSceneAction();
            }

            // Pause cinématique pour apprécier le titre
            float holdTimeRemaining = Mathf.Max(0.5f, _titleCardHoldDuration - titleFadeInTime);
            elapsed = 0f;
            while (elapsed < holdTimeRemaining)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            // Fade-out du carton de titre
            elapsed = 0f;
            const float titleFadeOutTime = 0.3f;
            while (elapsed < titleFadeOutTime)
            {
                elapsed += Time.unscaledDeltaTime;
                _titleCardAlpha = Mathf.Clamp01(1f - (elapsed / titleFadeOutTime));
                yield return null;
            }
            _titleCardAlpha = 0f;

            // --- PHASE 3 : Fondu de réouverture & rétraction des bandes ---
            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Open, 0.75f);
            }

            elapsed = 0f;
            while (elapsed < _fadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeInDuration);
                float smooth = t * t * (3f - 2f * t);
                OverlayAlpha = 1f - smooth;
                _letterboxHeight = Mathf.Lerp(_maxLetterboxHeight, 0f, smooth);
                yield return null;
            }

            OverlayAlpha = 0f;
            _letterboxHeight = 0f;
            IsTransitioning = false;
        }

        /// <summary>
        /// Affiche un carton de conclusion de campagne lorsqu'aucune scène suivante n'existe.
        /// </summary>
        public IEnumerator PlayCampaignCompleteRoutine(string finalVolume, string finalTitle, Action onDismiss)
        {
            IsTransitioning = true;
            _isCampaignEndCard = true;
            _campaignEndDismissAction = onDismiss;

            _cardVolume = string.IsNullOrWhiteSpace(finalVolume) ? "CAMPAGNE ACCOMPLIE" : finalVolume.ToUpperInvariant();
            _cardTitle = string.IsNullOrWhiteSpace(finalTitle) ? "Fin du Volume" : finalTitle;
            _cardCanonRef = "Toutes les scènes de cette séquence ont été complétées avec succès.";
            _cardStatus = "MISSION TERMINÉE";

            float elapsed = 0f;
            while (elapsed < _fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeOutDuration);
                OverlayAlpha = t * t * (3f - 2f * t);
                _letterboxHeight = Mathf.Lerp(0f, _maxLetterboxHeight * 1.2f, OverlayAlpha);
                yield return null;
            }
            OverlayAlpha = 1f;

            elapsed = 0f;
            while (elapsed < 0.4f)
            {
                elapsed += Time.unscaledDeltaTime;
                _titleCardAlpha = Mathf.Clamp01(elapsed / 0.4f);
                yield return null;
            }
            _titleCardAlpha = 1f;

            while (_isCampaignEndCard)
            {
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < _fadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeInDuration);
                OverlayAlpha = 1f - (t * t * (3f - 2f * t));
                _titleCardAlpha = OverlayAlpha;
                _letterboxHeight = Mathf.Lerp(_maxLetterboxHeight * 1.2f, 0f, t);
                yield return null;
            }

            OverlayAlpha = 0f;
            _titleCardAlpha = 0f;
            _letterboxHeight = 0f;
            IsTransitioning = false;
        }

        public void DismissCampaignEndCard()
        {
            _isCampaignEndCard = false;
            _campaignEndDismissAction?.Invoke();
            _campaignEndDismissAction = null;
        }

        private void OnGUI()
        {
            if (OverlayAlpha <= 0.001f && _titleCardAlpha <= 0.001f) return;

            InitTextures();
            InitStyles();

            GUI.depth = -10000;
            float sw = Screen.width;
            float sh = Screen.height;

            var e = Event.current;
            if (IsTransitioning && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel))
            {
                if (!_isCampaignEndCard) e.Use();
            }

            // 1. Fond obsidienne plein écran
            Color prevColor = GUI.color;
            Color backdropColor = new Color(0.024f, 0.035f, 0.055f, OverlayAlpha * 0.98f);
            GUI.color = backdropColor;
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _whiteTex);

            // 2. Bandes cinématiques (Letterboxing) noir pur
            if (_letterboxHeight > 0.5f)
            {
                GUI.color = new Color(0.01f, 0.01f, 0.015f, OverlayAlpha);
                GUI.DrawTexture(new Rect(0, 0, sw, _letterboxHeight), _whiteTex);
                GUI.DrawTexture(new Rect(0, sh - _letterboxHeight, sw, _letterboxHeight), _whiteTex);

                // Liseré cyan subtil au bord des bandes
                GUI.color = new Color(0.2f, 0.75f, 1f, OverlayAlpha * 0.35f);
                GUI.DrawTexture(new Rect(0, _letterboxHeight - 1f, sw, 1f), _whiteTex);
                GUI.DrawTexture(new Rect(0, sh - _letterboxHeight, sw, 1f), _whiteTex);
            }

            // 3. Carton de titre central
            if (_titleCardAlpha > 0.01f)
            {
                DrawTitleCard(sw, sh);
            }

            GUI.color = prevColor;
        }

        private void DrawTitleCard(float sw, float sh)
        {
            float cardWidth = Mathf.Min(680f, sw - 40f);
            float cardHeight = 260f;
            float cardX = (sw - cardWidth) * 0.5f;
            float cardY = (sh - cardHeight) * 0.46f;

            Color panelBg = new Color(0.05f, 0.08f, 0.13f, _titleCardAlpha * 0.88f);
            GUI.color = panelBg;
            GUI.DrawTexture(new Rect(cardX, cardY, cardWidth, cardHeight), _whiteTex);

            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 3.5f);
            Color borderColor = new Color(0.25f, 0.75f, 1f, _titleCardAlpha * 0.45f * pulse);
            DrawOutline(new Rect(cardX, cardY, cardWidth, cardHeight), borderColor, 1f);

            GUILayout.BeginArea(new Rect(cardX + 24f, cardY + 20f, cardWidth - 48f, cardHeight - 36f));
            GUILayout.BeginVertical();

            Color volumeColor = new Color(0.35f, 0.85f, 1f, _titleCardAlpha);
            GUI.color = volumeColor;
            string volumeText = _isCampaignEndCard
                ? $"<color=#F59E0B>✦ {_cardVolume} ✦</color>"
                : $"<color=#38BDF8>✦ {_cardVolume} ✦</color>";
            GUILayout.Label(volumeText, _volumeStyle);

            GUILayout.Space(6f);

            GUI.color = new Color(1f, 1f, 1f, _titleCardAlpha);
            string titleText = _isCampaignEndCard
                ? $"<color=#FCD34D><b>{_cardTitle}</b></color>"
                : $"<b>{_cardTitle}</b>";
            GUILayout.Label(titleText, _titleStyle);

            GUILayout.Space(6f);

            Rect sepRect = GUILayoutUtility.GetRect(cardWidth - 48f, 14f);
            float sepWidth = Mathf.Min(340f, cardWidth - 80f);
            float sepX = sepRect.x + (sepRect.width - sepWidth) * 0.5f;

            Color glowSepColor = _isCampaignEndCard
                ? new Color(0.96f, 0.72f, 0.2f, _titleCardAlpha * pulse)
                : new Color(0.22f, 0.8f, 1f, _titleCardAlpha * pulse);

            GUI.color = glowSepColor;
            GUI.DrawTexture(new Rect(sepX, sepRect.y + 6f, sepWidth, 2f), _gradientTex);

            GUI.color = new Color(1f, 1f, 1f, _titleCardAlpha * 0.9f);
            var glyphStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            GUI.Label(new Rect(sepX, sepRect.y, sepWidth, 14f), "◆   ◇   ◆", glyphStyle);

            GUILayout.Space(6f);

            if (!string.IsNullOrWhiteSpace(_cardCanonRef))
            {
                GUI.color = new Color(0.72f, 0.82f, 0.92f, _titleCardAlpha * 0.9f);
                GUILayout.Label($"<i>{_cardCanonRef}</i>", _subStyle);
            }

            GUILayout.FlexibleSpace();

            if (_isCampaignEndCard)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.25f, 0.85f, 0.5f, _titleCardAlpha);
                if (GUILayout.Button("✔ Conclure la Campagne & Fermer", GUILayout.Width(280), GUILayout.Height(34)))
                {
                    DismissCampaignEndCard();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                DrawLoadingBar(cardWidth - 48f);
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawLoadingBar(float availableWidth)
        {
            int dotCount = ((int)(Time.unscaledTime * 3f)) % 4;
            string dots = new string('.', dotCount);
            GUI.color = new Color(0.45f, 0.7f, 0.95f, _titleCardAlpha * 0.85f);
            GUILayout.Label($"{_cardStatus}{dots}", _statusStyle);

            GUILayout.Space(4f);

            float barWidth = Mathf.Min(260f, availableWidth - 40f);
            Rect barRect = GUILayoutUtility.GetRect(availableWidth, 4f);
            float startX = barRect.x + (barRect.width - barWidth) * 0.5f;

            GUI.color = new Color(0.12f, 0.18f, 0.28f, _titleCardAlpha * 0.6f);
            GUI.DrawTexture(new Rect(startX, barRect.y + 1f, barWidth, 2f), _whiteTex);

            float sweep = (Mathf.Sin(Time.unscaledTime * 4.2f) + 1f) * 0.5f;
            float sweepWidth = 70f;
            float headX = startX + sweep * (barWidth - sweepWidth);

            GUI.color = new Color(0.25f, 0.85f, 1f, _titleCardAlpha * 0.9f);
            GUI.DrawTexture(new Rect(headX, barRect.y, sweepWidth, 3f), _gradientTex);
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _whiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), _whiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _whiteTex);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), _whiteTex);
        }
    }
}
