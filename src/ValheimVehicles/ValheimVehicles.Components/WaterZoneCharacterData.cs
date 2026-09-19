#region

  using ValheimVehicles.Controllers;

#endregion

  namespace ValheimVehicles.Components;

  public interface ICharacterOnboardData
  {
    public bool IsUnderwater { get; set; }
    public bool IsWithinWaterMask { get; set; }
    public bool IsWithinShip { get; set; }
    public Character character { get; set; }
    public ZDOID zdoId { get; set; }
  }

  /// <summary>
  /// This is meant to keep additional Character specific data within each character to avoid conflicts.
  /// It does not extend character as this would create an additional pointer (and then require management in lifecycle).
  /// </summary>
  public class WaterZoneCharacterData : ICharacterOnboardData
  {
    public bool IsUnderwater { get; set; }
    public bool IsWithinWaterMask { get; set; }
    public bool IsWithinShip { get; set; }
    public Character character { get; set; }
    public ZDOID zdoId { get; set; }
    public ZDOID controllerZdoId { get; set; }

    // can turn null
    public VehicleOnboardController? OnboardController;

    public VehicleManager? VehicleShip =>
      OnboardController == null ? null : OnboardController.Manager;

    public WaterZoneController? WaterZoneController;
    private WaterVolume? _prevWaterVolume;

    public void UpdateUnderwaterStatus(bool? forceIsUnderwater = null)
    {
      if (forceIsUnderwater != null)
      {
        IsUnderwater = forceIsUnderwater.Value;
        return;
      }

      IsUnderwater =
        Floating.IsUnderWater(character.transform.position, ref _prevWaterVolume);
    }

    public bool IsSwimming
    {
      get
      {
        if (IsUnderwater) return false;
        return character.IsSwimming();
      }
    }

    public WaterZoneCharacterData(Character characterInstance,
      WaterZoneController? waterZoneController = null)
    {
      character = characterInstance;
      zdoId = character.GetZDOID();
      OnboardController = null;
      SetWaterZoneController(waterZoneController);
    }

    /// <summary>
    /// Points this record at the mask the character is currently inside.
    ///
    /// Previously the constructor did `waterZoneController = waterZoneController`, a
    /// self-assignment that left the field null, and then dereferenced the same nullable
    /// parameter three deep for the id. A mask whose ZNetView had no ZDO therefore threw
    /// out of OnTriggerEnter before the character was ever registered, silently leaving
    /// them outside the water-free zone.
    /// </summary>
    public void SetWaterZoneController(WaterZoneController? controller)
    {
      WaterZoneController = controller;

      var controllerNetView = controller != null
        ? controller.GetComponent<ZNetView>()
        : null;
      var controllerZdo = controllerNetView != null
        ? controllerNetView.GetZDO()
        : null;

      controllerZdoId = controllerZdo?.m_uid ?? ZDOID.None;
    }

    public WaterZoneCharacterData(Character characterInstance,
      VehicleOnboardController? onboardControllerInstance)
    {
      character = characterInstance;
      zdoId = character.GetZDOID();
      OnboardController = onboardControllerInstance;
    }
  }