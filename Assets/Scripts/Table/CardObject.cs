using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Holdem;

namespace HoldemTable
{
    /// <summary>
    /// One physical card lying on the table, built from two quads (front and back).
    /// Face-up = just the yaw rotation; face-down = yaw, then a 180 degree flip about the card's own X axis.
    /// The card's top edge points along its local +Z axis.
    /// The art is a 42x60 card in the middle of a 64x64 PNG, so the texture is cropped to the card.
    /// </summary>
    public class CardObject : MonoBehaviour
    {
        public const float Width = 0.7f, Height = 1.0f;

        /// <summary>
        /// Which way a card turns over. The turn is a rotation about the card's own X axis (its left-right line).
        /// false: face-up = 0 degrees, so turning it up lifts the edge nearest the player.
        /// true:  face-up = 360 degrees (same picture, opposite spin), so turning it up lifts the far edge instead.
        /// The table's "reverseFlip" tickbox sets this.
        /// </summary>
        public static bool FlipReversed = true;
        private static float UpAngle => FlipReversed ? 360f : 0f;
        private const float DownAngle = 180f;

        public Card Card { get; private set; }

        /// <summary>True when the card is, or is on its way to being, face-up.</summary>
        public bool FaceUp => _destFaceUp;

        private Quaternion _yaw = Quaternion.identity;
        private float _flip = DownAngle; // UpAngle (0 or 360) = face up, 180 = face down

        // Where the current motion is heading. A new motion replaces the old one, so a flip that is
        // requested while the card is still flying simply redirects it instead of fighting with it.
        private int _motion;
        private Vector3 _destPos;
        private Quaternion _destYaw = Quaternion.identity;
        private bool _destFaceUp;

        private static readonly Vector2 CropScale = new Vector2(42f / 64f, 60f / 64f);
        private static readonly Vector2 CropOffset = new Vector2(11f / 64f, 2f / 64f);
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static bool _warnedMissing;

        // ------------------------------------------------------------ creation
        public static CardObject Create(Card card, Transform parent)
        {
            var go = new GameObject("Card " + card.AssetName);
            go.transform.SetParent(parent, false);
            var co = go.AddComponent<CardObject>();
            co.Build(card);
            return co;
        }

        private void Build(Card card)
        {
            Card = card;
            MakeFace("Front", Quaternion.Euler(90f, 0f, 0f), 0.0005f, GetMaterial(card.AssetName));
            MakeFace("Back", Quaternion.Euler(-90f, 0f, 0f), -0.0005f, GetMaterial("card_back"));

            // one thin collider on the card itself so the table can tell which card was clicked
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(Width, 0.03f, Height);
            Apply();
        }

        private void MakeFace(string faceName, Quaternion rotation, float y, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = faceName;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localRotation = rotation;
            quad.transform.localPosition = new Vector3(0f, y, 0f);
            quad.transform.localScale = new Vector3(Width, Height, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material GetMaterial(string assetName)
        {
            Material m;
            if (Cache.TryGetValue(assetName, out m) && m != null) return m;

            var tex = Resources.Load<Texture2D>("Cards/" + assetName);
            if (tex == null)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Debug.LogWarning("Card texture not found: Resources/Cards/" + assetName +
                                     ".png (copy your card PNGs into Assets/Resources/Cards/). Showing blank cards.");
                }
                m = TableMaterials.Card(null, CropScale, CropOffset);
            }
            else
            {
                tex.filterMode = FilterMode.Point;       // crisp pixel art
                tex.wrapMode = TextureWrapMode.Clamp;
                m = TableMaterials.Card(tex, CropScale, CropOffset);
            }
            Cache[assetName] = m;
            return m;
        }

        // ------------------------------------------------------------ movement
        private void Apply()
        {
            transform.rotation = _yaw * Quaternion.Euler(_flip, 0f, 0f);
        }

        public void SetImmediate(Vector3 position, Quaternion yaw, bool faceUp)
        {
            _motion++; // cancels any motion in progress
            _destPos = position;
            _destYaw = yaw;
            _destFaceUp = faceUp;
            transform.position = position;
            _yaw = yaw;
            _flip = faceUp ? UpAngle : DownAngle;
            Apply();
        }

        /// <summary>Slides the card to a spot on a small arc, turning it face-up or face-down on the way. Replaces any earlier motion.</summary>
        public IEnumerator MoveTo(Vector3 position, Quaternion yaw, bool faceUp, float duration, float lift = 0.6f)
        {
            int id = ++_motion;
            _destPos = position;
            _destYaw = yaw;
            _destFaceUp = faceUp;
            return Animate(id, position, yaw, faceUp, duration, lift);
        }

        private IEnumerator Animate(int id, Vector3 position, Quaternion yaw, bool faceUp, float duration, float lift)
        {
            Vector3 p0 = transform.position;
            Quaternion y0 = _yaw;
            float f0 = _flip;
            float f1 = faceUp ? UpAngle : DownAngle;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, duration);
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                transform.position = Vector3.Lerp(p0, position, e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * lift);
                _yaw = Quaternion.Slerp(y0, yaw, e);
                _flip = Mathf.Lerp(f0, f1, e);
                Apply();
                yield return null;
                if (id != _motion) yield break; // a newer motion took over
            }
            transform.position = position;
            _yaw = yaw;
            _flip = f1;
            Apply();
        }

        /// <summary>Turns the card over where it is going to lie (or lies).</summary>
        public IEnumerator Flip(bool faceUp, float duration)
        {
            return MoveTo(_destPos, _destYaw, faceUp, duration, 0.6f);
        }

        /// <summary>Slides the card (face-down) to a spot, then removes it (used when a player folds).</summary>
        public IEnumerator MoveAndDestroy(Vector3 position, float duration)
        {
            yield return StartCoroutine(MoveTo(position, _destYaw, false, duration, 0.4f));
            Destroy(gameObject);
        }
    }
}