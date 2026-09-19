using System.Collections.Generic;
using UnityEngine;
using ValheimVehicles.Prefabs;
using ValheimVehicles.SharedScripts;
using Zolantris.Shared;

namespace ValheimVehicles.Components;

public class CreativeModeColliderComponent : MonoBehaviour
{
  public BoxCollider collider;
  private static List<CreativeModeColliderComponent> Instances = new();
  public static bool IsEditMode = false;
  private static Material? _cubeMaskMaterial;

  public Material CubeMaskMaterial => GetCubeMaskMaterial();

  public Material GetCubeMaskMaterial()
  {
    if (_cubeMaskMaterial == null)
    {
      _cubeMaskMaterial = LoadValheimVehicleAssets.TransparentDepthMaskMaterial;
    }

    return _cubeMaskMaterial;
  }

  internal void Awake()
  {
    if (ZNetView.m_forceDisableInit)
    {
      return;
    }

    Instances.Add(this);
    // Must be resolved before SetMode, which no-ops on a null collider.
    collider = GetComponent<BoxCollider>();
    SetMode(IsEditMode);
  }

  internal void OnDestroy()
  {
    Instances.Remove(this);
  }

  /// <summary>
  /// Enables the box collider which allows for editing the watermask, otherwise the user will not be able to interact with box/delete it.
  ///
  /// The character layers are always kept collidable. This collider is a trigger whose
  /// whole purpose is to notice characters entering the zone, and PhysicalLayerMask
  /// contains "character" - excluding it switched the water zone off for every mask the
  /// moment edit mode was toggled, with nothing to ever switch it back on.
  /// </summary>
  /// <param name="isEditMode"></param>
  public void SetMode(bool isEditMode)
  {
    if (collider == null) return;

    collider.excludeLayers = isEditMode
      ? LayerHelpers.PhysicalLayerMask.value & ~LayerHelpers.CharacterLayerMask
      : 0;
  }

  public virtual void OnToggleEditMode()
  {
  }

  public static void ToggleEditMode()
  {
    IsEditMode = !IsEditMode;
    foreach (var creativeModeColliderComponent in Instances)
    {
      creativeModeColliderComponent.SetMode(IsEditMode);
      creativeModeColliderComponent.OnToggleEditMode();
    }
  }
}