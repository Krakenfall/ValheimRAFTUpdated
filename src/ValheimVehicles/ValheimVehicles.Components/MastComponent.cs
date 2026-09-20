using System;
using System.Reflection;
using UnityEngine;
using Logger = Jotunn.Logger;

namespace ValheimVehicles.Components;

public class MastComponent : MonoBehaviour
{
  public GameObject? m_sailObject;

  public Cloth? m_sailCloth;

  public bool m_allowSailRotation = false;
  public Transform? m_rotationTransform = null;

  public bool m_allowSailShrinking = true;

  public bool m_disableCloth;

  /// <summary>
  /// Valheim 1.0 stopped scaling vanilla sails. Furling now slides the bottom edge of the
  /// sail between three marker transforms while MagicaCloth simulates the sheet. The sail
  /// object's pivot sits at the mast base, so the old scale-based shrinking collapses the
  /// whole sail down onto the base instead of gathering it up against the yard.
  /// </summary>
  public Transform? m_sailBottomTransform;

  public Transform? m_sailFurledPosition;
  public Transform? m_sailMidFurledPosition;
  public Transform? m_sailUnfurledPosition;

  /// <summary>
  /// MagicaCloth2 component driving the vanilla sail. Typed as Component so the mod does
  /// not take a compile-time dependency on MagicaClothV2 - see <see cref="ApplyClothBlendWeight" />.
  /// </summary>
  public Component? m_magicaSailCloth;

  /// <summary>Copied off the source ship so furled sails stiffen exactly like vanilla.</summary>
  public AnimationCurve? m_sailBlendWeightCurve;

  private float m_appliedBlendWeightPosition = float.NaN;

  /// <summary>
  /// True once the Valheim 1.0 furl markers resolved. Masts without them (custom mod masts,
  /// pre-1.0 game versions) keep using the legacy scale-based shrinking.
  /// </summary>
  public bool HasVanillaFurlPositions =>
    m_sailBottomTransform != null && m_sailFurledPosition != null &&
    m_sailMidFurledPosition != null && m_sailUnfurledPosition != null;

  // for custom masts. Other masts do not support this. We may need to add a selector to make this cleaner.
  public void Awake()
  {
    m_rotationTransform = transform.Find("rotational_yard");

    // Ensure rotation transform is properly initialized even if it's null
    if (m_rotationTransform == null)
    {
      Logger.LogDebug($"MastComponent.Awake(): Could not find 'rotational_yard' transform on mast. This may cause sail attachment issues.");
    }
  }

  /// <summary>
  /// Mirrors Ship.UpdateSailSize for a single mast. <paramref name="sailPosition" /> is 0 for a
  /// fully furled sail and 1 for a fully unfurled one; the two marker pairs each cover half of
  /// that range. Must run every frame because the markers move with the vehicle.
  /// </summary>
  public void UpdateSailFurlPosition(float sailPosition)
  {
    if (!HasVanillaFurlPositions) return;

    sailPosition = Mathf.Clamp01(sailPosition);

    var from = m_sailFurledPosition!.position;
    var to = m_sailMidFurledPosition!.position;
    if (sailPosition >= 0.5f)
    {
      from = m_sailMidFurledPosition.position;
      to = m_sailUnfurledPosition!.position;
    }

    // fractional part of (position * 2) - splits 0..1 across the two marker pairs.
    var scaled = Mathf.Clamp(sailPosition, 0f, 0.999f) * 2f;
    m_sailBottomTransform!.position =
      Vector3.Lerp(from, to, scaled - Mathf.Floor(scaled));

    if (Mathf.Approximately(m_appliedBlendWeightPosition, sailPosition)) return;
    m_appliedBlendWeightPosition = sailPosition;
    ApplyClothBlendWeight(sailPosition);
  }

  private static bool s_magicaReflectionFailed;
  private static PropertyInfo? s_serializeDataProperty;
  private static FieldInfo? s_blendWeightField;
  private static MethodInfo? s_setParameterChangeMethod;

  /// <summary>
  /// Vanilla fades the MagicaCloth blend weight with the sail position so a furled sail stops
  /// billowing. Done reflectively to keep MagicaClothV2 out of the mod's reference set - if the
  /// API ever moves, the sail still furls, it just keeps simulating.
  /// </summary>
  private void ApplyClothBlendWeight(float sailPosition)
  {
    if (m_magicaSailCloth == null || m_sailBlendWeightCurve == null) return;
    if (s_magicaReflectionFailed) return;

    try
    {
      if (s_serializeDataProperty == null)
      {
        var clothType = m_magicaSailCloth.GetType();
        s_serializeDataProperty = clothType.GetProperty("SerializeData");
        s_setParameterChangeMethod =
          clothType.GetMethod("SetParameterChange", Type.EmptyTypes);
        s_blendWeightField = s_serializeDataProperty?.PropertyType
          .GetField("blendWeight");

        if (s_serializeDataProperty == null || s_setParameterChangeMethod == null ||
            s_blendWeightField == null)
        {
          s_magicaReflectionFailed = true;
          Logger.LogDebug(
            "MastComponent: MagicaCloth blend weight API not found, sail cloth stiffness will not follow the furl position.");
          return;
        }
      }

      var serializeData = s_serializeDataProperty.GetValue(m_magicaSailCloth, null);
      if (serializeData == null) return;

      s_blendWeightField!.SetValue(serializeData,
        Mathf.Clamp01(m_sailBlendWeightCurve.Evaluate(sailPosition)));
      s_setParameterChangeMethod!.Invoke(m_magicaSailCloth, null);
    }
    catch (Exception e)
    {
      s_magicaReflectionFailed = true;
      Logger.LogDebug($"MastComponent: failed to update MagicaCloth blend weight \n{e}");
    }
  }
}
