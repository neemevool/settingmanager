namespace VTMF.Settings.Entities;

/// <summary>
/// Represents the state of a feature within a process.
/// </summary>
public enum FeatureState
{
    /// <summary>
  /// Feature is available but not yet applied.
    /// </summary>
    Available = 0,

    /// <summary>
    /// Feature is currently being applied.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Feature has been successfully applied.
  /// </summary>
    Applied = 2
}
