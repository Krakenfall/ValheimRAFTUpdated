using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Serialization;
using ValheimVehicles.Attributes;
using ValheimVehicles.Components;
using ValheimVehicles.BepInExConfig;
using ValheimVehicles.Helpers;
using ValheimVehicles.Prefabs;
using ValheimVehicles.SharedScripts;
using ValheimVehicles.Controllers;
using ValheimVehicles.Shared.Constants;
using ValheimVehicles.Structs;
using Zolantris.Shared;
using Logger = Jotunn.Logger;

namespace ValheimVehicles.Components;

public class WaterZoneController : CreativeModeColliderComponent
{
  public ZNetView? netView;
  public static readonly Dictionary<ZDOID, WaterZoneController> Instances = [];

  private VehicleDebugHelpers? _debugComponent;

  public static Dictionary<ZDOID, WaterZoneCharacterData>
    WaterZoneCharacterData = new();

  private ZDOID instanceZdoid = ZDOID.None;

  // can be null as this may not be the type of controller
  private VehicleOnboardController? _onboardController = null;

  public enum WaterZoneControllerType
  {
    Vehicle,
    Static
  }

  public WaterZoneControllerType zoneType = WaterZoneControllerType.Static;
  public Vector3 defaultScale = Vector3.one;

  private new void Awake()
  {
    base.Awake();
    netView = GetComponent<ZNetView>();
    _onboardController = GetComponentInParent<VehicleOnboardController>();
  }

  private bool _hasInitializedMask;
  private Coroutine? _pendingInitCoroutine;

  public void Start()
  {
    if (ZNetView.m_forceDisableInit) return;

    if (_onboardController) zoneType = WaterZoneControllerType.Vehicle;

    if (TryInitializeMask()) return;

    // Start() only ever fires once, so a ZDO that is not ready yet would leave the mask
    // stuck at its prefab scale - a 1m cube - for the rest of its lifetime. Retry instead.
    _pendingInitCoroutine ??= StartCoroutine(WaitForZdoThenInitialize());
  }

  private System.Collections.IEnumerator WaitForZdoThenInitialize()
  {
    var attempts = 0;
    // ~10s at 50hz. Generous enough to outlast zone streaming, bounded so a mask whose
    // ZDO never arrives cannot spin for the whole session.
    while (attempts < 500 && !_hasInitializedMask)
    {
      attempts++;
      yield return new WaitForFixedUpdate();
      if (TryInitializeMask()) break;
    }

    _pendingInitCoroutine = null;

    if (!_hasInitializedMask)
      Logger.LogWarning(
        $"Water mask {name} never received a valid ZDO, so its size could not be restored.");
  }

  /// <summary>
  /// Registers the instance and applies the stored mask volume.
  /// </summary>
  /// <returns>true once the mask has been sized from its ZDO.</returns>
  private bool TryInitializeMask()
  {
    if (_hasInitializedMask) return true;
    if (ZNetView.m_forceDisableInit) return false;

    if (netView == null) netView = GetComponent<ZNetView>();
    // Guarded deliberately: this used to dereference GetZDO() directly, so a not-yet-ready
    // ZDO threw out of Start() before the mask was ever sized.
    var zdo = netView != null ? netView.GetZDO() : null;
    if (zdo == null) return false;

    instanceZdoid = zdo.m_uid;
    if (!Instances.ContainsKey(instanceZdoid))
      Instances.Add(instanceZdoid, this);

    InitMaskFromNetview();

    _hasInitializedMask = true;
    return true;
  }

  private static bool IsInWaterFreeZone(Character character)
  {
    return WaterZoneCharacterData.ContainsKey(character.GetZDOID());
  }

  // todo to make an overload that takes vehicleonboard controller.
  public static bool GetWaterZoneController(Character character,
    out WaterZoneController? waterZoneController
    // out VehicleOnboardController? onboardController
  )
  {
    waterZoneController = null;
    if (WaterZoneCharacterData.TryGetValue(character.GetZDOID(),
          out var characterData))
    {
      waterZoneController = characterData.WaterZoneController;
      return characterData.WaterZoneController != null;
    }
    //
    //
    // if (VehicleOnboardController.GetCharacterVehicleMovementController(zdoid,
    //       out VehicleOnboardController? vehicleOnboardController))
    // {
    //   onboardController = vehicleOnboardController;
    //   return true;
    // }

    return false;
  }

  /// <summary>
  /// Main calc for checking zones where player can be underwater
  /// </summary>
  /// <param name="character"></param>
  /// <returns></returns>
  /// <exception cref="ArgumentOutOfRangeException"></exception>
  [GameCacheValue]
  public static bool IsCharacterInWaterFreeZone(Character character)
  {
    return WaterConfig.UnderwaterAccessMode.Value switch
    {
      WaterConfig.UnderwaterAccessModeType.Disabled => false,
      WaterConfig.UnderwaterAccessModeType.Everywhere => true,
      WaterConfig.UnderwaterAccessModeType.OnboardOnly =>
        WaterZoneUtils.IsOnboard(character),
      WaterConfig.UnderwaterAccessModeType.DEBUG_WaterZoneOnly =>
        IsInWaterFreeZone(
          character),
      _ => throw new ArgumentOutOfRangeException()
    };
  }

  /// <summary>
  /// To be combined with onboard data. Avoid complicated logic delegating to other controllers.
  /// </summary>
  /// <param name="character"></param>
  /// <param name="waterZoneData"></param>
  /// <returns></returns>
  /// <exception cref="ArgumentOutOfRangeException"></exception>
  public static bool GetCharacterDataFromWaterZone(Character character,
    out WaterZoneCharacterData? waterZoneData)
  {
    waterZoneData = null;
    switch (WaterConfig.UnderwaterAccessMode.Value)
    {
      case WaterConfig.UnderwaterAccessModeType.Disabled:
      case WaterConfig.UnderwaterAccessModeType.Everywhere:
        waterZoneData = null;
        return false;
      case WaterConfig.UnderwaterAccessModeType.OnboardOnly:
        waterZoneData =
          VehicleOnboardController.GetOnboardCharacterData(character);
        return waterZoneData != null;
      case WaterConfig.UnderwaterAccessModeType.DEBUG_WaterZoneOnly:
        waterZoneData = GetCharacterWaterZoneData(character);
        return waterZoneData != null;
      default:
        throw new ArgumentOutOfRangeException();
    }
  }

  public static WaterZoneCharacterData? GetCharacterWaterZoneData(
    Character character)
  {
    WaterZoneCharacterData.TryGetValue(character.GetZDOID(), out var data);
    return data;
  }

  private new void OnDestroy()
  {
    base.OnDestroy();
    Instances.Remove(instanceZdoid);
  }

  public void OnTriggerEnter(Collider collider)
  {
    var character = collider.GetComponent<Character>();
    if (character == null) return;
    var characterZdoid = character.GetZDOID();

    // we do not need to keep transitioning the player between areas. This avoids an exit/entry call continuously fighting for ownership
    if (WaterZoneCharacterData.ContainsKey(characterZdoid)) return;

    WaterZoneCharacterData.Add(characterZdoid,
      new WaterZoneCharacterData(character, this));
  }

  public void OnTriggerExit(Collider collider)
  {
    var character = collider.GetComponent<Character>();
    if (character == null) return;

    if (!WaterZoneCharacterData.TryGetValue(instanceZdoid, out var data))
      return;

    // only removes the instance associated with it.
    if (data.controllerZdoId != instanceZdoid &&
        Instances.ContainsKey(instanceZdoid)) return;
    WaterZoneCharacterData.Remove(instanceZdoid);
  }

  public static void OnToggleEditMode(bool isDebug)
  {
    foreach (var waterMaskComponent in Instances.Values.ToList())
      if (isDebug)
        waterMaskComponent?.UseDebugComponents();
      else
        waterMaskComponent?.UseHiddenComponents();
  }

  private void CreateDebugHelperComponent()
  {
    if (_debugComponent == null)
      _debugComponent = gameObject.AddComponent<VehicleDebugHelpers>();

    if (!collider) collider = GetComponent<BoxCollider>();

    if (collider == null) return;

    _debugComponent.AddColliderToRerender(new DrawTargetColliders
    {
      collider = collider,
      parent = transform,
      lineColor = new Color(0, 0.5f, 1f, 0.8f),
      width = 1f
    });
    _debugComponent.autoUpdateColliders = true;
  }

  /// <summary>
  /// Meant to show the mask in a "Debug mode" by swapping shaders
  /// Makes the component breakable
  /// </summary>
  public void UseDebugComponents()
  {
    CreateDebugHelperComponent();
    gameObject.layer = LayerHelpers.PieceNonSolidLayer;
  }

  /// <summary>
  /// Makes the component untouchable.
  /// </summary>
  public void UseHiddenComponents()
  {
    if (_debugComponent != null) Destroy(_debugComponent);

    gameObject.layer = LayerHelpers.IgnoreRaycastLayer;
  }

  private static PrimitiveType GetPrimitiveTypeFromZdo(ZDO zdo)
  {
    var primitiveType = zdo.GetInt(
      VehicleZdoVars.CustomMeshPrimitiveType,
      -1);

    // update zdo if invalid
    if (primitiveType == -1)
    {
      zdo.Set(VehicleZdoVars.CustomMeshPrimitiveType,
        (int)PrimitiveType.Cube);
      return PrimitiveType.Cube;
    }

    return (PrimitiveType)primitiveType;
  }

  /// <summary>
  /// ZDO has no "does this key exist" API, so probe it with two different defaults.
  /// They can only agree when the key is actually present.
  ///
  /// This matters because a missing size and a stored size of zero mean opposite things:
  /// a stored zero is a degenerate mask that should be removed, while a missing key must
  /// never be treated as a real size - doing so is what collapses a mask into a 1m cube.
  /// </summary>
  private static bool TryGetStoredSize(ZDO zdo, out Vector3 size)
  {
    size = zdo.GetVec3(VehicleZdoVars.CustomMeshScale, Vector3.zero);
    return size == zdo.GetVec3(VehicleZdoVars.CustomMeshScale, Vector3.one);
  }

  /// <summary>
  /// Resolves the volume this mask should occupy, in priority order:
  ///
  /// 1. The mod's own stored size.
  /// 2. The scale ZNetView already restored from the engine's own scale key during Awake
  ///    (see m_syncInitialScale on the prefab). This is the safety net for masks whose
  ///    stored size went missing - previously that case silently fell through to
  ///    <see cref="defaultScale"/> and shrank the mask to a 1m cube on load.
  /// 3. <see cref="defaultScale"/>, which test prefabs set explicitly.
  /// </summary>
  private bool TryResolveMaskSize(ZDO zdo, out Vector3 size)
  {
    if (TryGetStoredSize(zdo, out size))
    {
      if (size == Vector3.zero) return false;

      // Mirror the size into the engine's scale key so ZNetView.Awake can restore it
      // directly next load, without depending on this component running at all.
      if (netView != null && netView.IsOwner() &&
          zdo.GetVec3(ZDOVars.s_scaleHash, Vector3.zero) != size)
      {
        zdo.Set(ZDOVars.s_scaleHash, size);
      }

      return true;
    }

    var currentScale = transform.localScale;
    if (currentScale != Vector3.one && currentScale != Vector3.zero)
    {
      Logger.LogWarning(
        $"Water mask {name} has no stored size. Falling back to its restored scale {currentScale}.");
      size = currentScale;
      return true;
    }

    size = defaultScale;
    return size != Vector3.zero;
  }

  public void InitPrimitive()
  {
    var zdo = netView!.GetZDO();
    if (zdo == null) return;
    var primitiveType = GetPrimitiveTypeFromZdo(zdo);

    if (!TryResolveMaskSize(zdo, out var size))
    {
      // invalid mesh, destroy it
      Destroy(gameObject);
      return;
    }

    var renderer = GetComponent<MeshRenderer>();
    var meshFilter = GetComponent<MeshFilter>();
    collider = GetComponent<BoxCollider>();
    collider.isTrigger = true;
    collider.includeLayers = LayerHelpers.CharacterLayer;

    var primitive = GameObject.CreatePrimitive(primitiveType);
    var primitiveMeshFilter = primitive.GetComponent<MeshFilter>();

    gameObject.layer = LayerHelpers.IgnoreRaycastLayer;
    renderer.sharedMaterial = CubeMaskMaterial;
    meshFilter.sharedMesh = primitiveMeshFilter.sharedMesh;
    transform.localScale = size;

    Destroy(primitive);
  }

  public void InitMaskFromNetview()
  {
    if (ZNetView.m_forceDisableInit || netView?.GetZDO() == null) return;
    InitPrimitive();

    if (IsEditMode)
      UseDebugComponents();
    else
      UseHiddenComponents();
  }
}