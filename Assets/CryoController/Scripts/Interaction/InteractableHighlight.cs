using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CryoController
{
    /// <summary>
    /// Tints an interactable's renderers while the player aims at it, so it reads as something to use. Put it on the
    /// object with the interactable, or on a parent of it; <see cref="PlayerInteractor"/> switches it on and off. The
    /// tint goes through a MaterialPropertyBlock per material slot on the base colour (_BaseColor in URP and HDRP, _Color
    /// in the Built-in pipeline), so no material is changed or copied. Property blocks other scripts have set are kept:
    /// only the colour is tinted, and it is put back when the highlight fades out.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CryoController/Interactable Highlight")]
    public class InteractableHighlight : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Colour blended into the object's own while it is aimed at.")]
        [SerializeField] private Color color = new Color(1f, 0.92f, 0.6f, 1f);
        [Tooltip("How far the object's colour moves towards Color: 0 not at all, 1 all the way.")]
        [SerializeField, Range(0f, 1f)] private float strength = 0.4f;
        [Tooltip("Seconds the highlight takes to fade in or out.")]
        [SerializeField, Min(0f)] private float fadeTime = 0.1f;
        [Tooltip("Renderers to tint. Empty tints every renderer on this object and its children.")]
        [SerializeField] private Renderer[] renderers;

        /// <summary>True while the player aims at the object.</summary>
        public bool IsHighlighted { get; private set; }

        // A material slot being tinted, and the colours its property block held before (another script's, or the
        // renderer-wide block's when the slot had none of its own).
        private struct Slot
        {
            public Renderer Renderer;
            public int Index;
            public bool HadBlock;
            public bool HadBaseColor;
            public Color BaseColor;
            public bool HadColor;
            public Color Color;
        }

        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<Material> materials = new List<Material>();
        private MaterialPropertyBlock block;
        private float amount;   // 0..1, how far the highlight has faded in
        private bool lit;       // the slots are captured and tinted
        private Coroutine fade;

        private void Awake()
        {
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }
            block = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            if (fade != null)
            {
                StopCoroutine(fade);   // disabling the component alone would leave it running
                fade = null;
            }
            IsHighlighted = false;
            amount = 0f;
            if (lit)
            {
                Restore();
            }
        }

        /// <summary>Switches the highlight on or off. It fades over Fade Time, in unscaled time, so it also fades while paused.</summary>
        public void SetHighlighted(bool highlighted)
        {
            IsHighlighted = highlighted;
            if (!isActiveAndEnabled || block == null)
            {
                return;   // nothing is lit while disabled
            }
            if (fadeTime <= 0f)
            {
                if (fade != null)
                {
                    StopCoroutine(fade);
                    fade = null;
                }
                SetAmount(highlighted ? 1f : 0f);
            }
            else if (fade == null)
            {
                fade = StartCoroutine(Fade());
            }
        }

        // Runs only while fading, so an idle highlight costs nothing per frame.
        private IEnumerator Fade()
        {
            float target = IsHighlighted ? 1f : 0f;
            while (amount != target)
            {
                SetAmount(Mathf.MoveTowards(amount, target, Time.unscaledDeltaTime / Mathf.Max(fadeTime, 0.0001f)));
                yield return null;
                target = IsHighlighted ? 1f : 0f;
            }
            fade = null;
        }

        private void SetAmount(float value)
        {
            amount = value;
            if (amount > 0f)
            {
                if (!lit)
                {
                    Capture();
                }
                Tint();
            }
            else if (lit)
            {
                Restore();
            }
        }

        // Notes what every slot's property block held before the first tint, so it can be put back afterwards.
        private void Capture()
        {
            slots.Clear();
            if (renderers != null)
            {
                foreach (Renderer target in renderers)
                {
                    if (target == null)
                    {
                        continue;
                    }
                    target.GetSharedMaterials(materials);
                    for (int i = 0; i < materials.Count; i++)
                    {
                        target.GetPropertyBlock(block, i);
                        bool hadBlock = !block.isEmpty;
                        if (!hadBlock)
                        {
                            target.GetPropertyBlock(block);   // a slot block replaces the renderer-wide one, so start from it
                        }
                        slots.Add(new Slot
                        {
                            Renderer = target,
                            Index = i,
                            HadBlock = hadBlock,
                            HadBaseColor = block.HasColor(BaseColorId),
                            BaseColor = block.GetColor(BaseColorId),
                            HadColor = block.HasColor(ColorId),
                            Color = block.GetColor(ColorId),
                        });
                    }
                }
            }
            lit = true;
        }

        private void Tint()
        {
            float blend = amount * strength;
            Renderer current = null;
            foreach (Slot slot in slots)
            {
                if (slot.Renderer == null)
                {
                    continue;
                }
                if (slot.Renderer != current)
                {
                    current = slot.Renderer;
                    current.GetSharedMaterials(materials);   // read each time, so a swapped material (another pipeline's) is honoured
                }
                Material material = slot.Index < materials.Count ? materials[slot.Index] : null;
                if (material == null)
                {
                    continue;
                }
                bool hasBaseColor = material.HasProperty(BaseColorId);
                bool hasColor = material.HasProperty(ColorId);
                if (!hasBaseColor && !hasColor)
                {
                    continue;
                }

                slot.Renderer.GetPropertyBlock(block, slot.Index);
                if (block.isEmpty)
                {
                    slot.Renderer.GetPropertyBlock(block);
                }
                if (hasBaseColor)
                {
                    block.SetColor(BaseColorId, Tinted(slot.HadBaseColor ? slot.BaseColor : material.GetColor(BaseColorId), blend));
                }
                if (hasColor)
                {
                    block.SetColor(ColorId, Tinted(slot.HadColor ? slot.Color : material.GetColor(ColorId), blend));
                }
                slot.Renderer.SetPropertyBlock(block, slot.Index);
            }
        }

        // Blends towards the highlight colour but keeps the original alpha, so transparent objects stay as clear.
        private Color Tinted(Color original, float blend)
        {
            Color tinted = Color.Lerp(original, color, blend);
            tinted.a = original.a;
            return tinted;
        }

        private void Restore()
        {
            Renderer current = null;
            foreach (Slot slot in slots)
            {
                if (slot.Renderer == null)
                {
                    continue;
                }
                if (!slot.HadBlock)
                {
                    slot.Renderer.SetPropertyBlock(null, slot.Index);   // back to the renderer-wide block, if any
                    continue;
                }

                // The slot had a block of its own: put its colours back. One it did not have is set to the material's
                // own, which looks the same.
                if (slot.Renderer != current)
                {
                    current = slot.Renderer;
                    current.GetSharedMaterials(materials);
                }
                Material material = slot.Index < materials.Count ? materials[slot.Index] : null;
                if (material == null)
                {
                    continue;
                }
                slot.Renderer.GetPropertyBlock(block, slot.Index);
                if (material.HasProperty(BaseColorId))
                {
                    block.SetColor(BaseColorId, slot.HadBaseColor ? slot.BaseColor : material.GetColor(BaseColorId));
                }
                if (material.HasProperty(ColorId))
                {
                    block.SetColor(ColorId, slot.HadColor ? slot.Color : material.GetColor(ColorId));
                }
                slot.Renderer.SetPropertyBlock(block, slot.Index);
            }
            slots.Clear();
            lit = false;
        }
    }
}
