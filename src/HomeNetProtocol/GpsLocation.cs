using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetProtocol
{
  /// <summary>
  /// GPS location information and implementation of conversions between different representations.
  /// </summary>
  public class GpsLocation
  {
    /// <summary>Conversion factor between floating point representation and LocationType.</summary>
    public const decimal LocationTypeFactor = 1000000;

    /// <summary>Special LocationType value that represents no location.</summary>
    public const int NoLocation = unchecked((int)0xFFFFFFFF);

    /// <summary>Minimal value of latitude in floating point representation.</summary>
    public const decimal LatitudeMin = -90;

    /// <summary>Maximal value of latitude in floating point representation.</summary>
    public const decimal LatitudeMax = 90;

    /// <summary>Minimal value of longitude in floating point representation.</summary>
    public const decimal LongitudeMin = -180;

    /// <summary>Maximal value of longitude in floating point representation.</summary>
    public const decimal LongitudeMax = 180;

    /// <summary>GPS latitude is a number in range [-90;90].</summary>
    public decimal Latitude;

    /// <summary>GPS longitude is a number in range [-180;180].</summary>
    public decimal Longitude;

    /// <summary>
    /// Initializes GPS location information from floating point values.
    /// </summary>
    /// <param name="Latitude">Floating point latitude information. The valid range of values is [-90;90].</param>
    /// <param name="Longitude">Floating point longitude information. The valid range of values is [-180;180].</param>
    public GpsLocation(decimal Latitude, decimal Longitude)
    {
      this.Latitude = Latitude;
      this.Longitude = Longitude;
    }

    /// <summary>
    /// Initializes GPS location information from the LocationType values.
    /// </summary>
    /// <param name="Latitude">LocationType value of latitude information. The valid range of values is [-90,000,000;90,000,000].</param>
    /// <param name="Longitude">LocationType value of longitude information. The valid range of values is [-180,000,000;180,000,000].</param>
    public GpsLocation(int Latitude, int Longitude)
    {
      this.Latitude = (decimal)Latitude / LocationTypeFactor;
      this.Longitude = (decimal)Longitude / LocationTypeFactor;
    }

    /// <summary>
    /// Obtains latitude in LocationType representation.
    /// </summary>
    /// <returns>LocationType latitude information.</returns>
    public int GetLocationTypeLatitude()
    {
      return (int)(Latitude * LocationTypeFactor);
    }

    /// <summary>
    /// Obtains longitude in LocationType representation.
    /// </summary>
    /// <returns>LocationType longitude information.</returns>
    public int GetLocationTypeLongitude()
    {
      return (int)(Longitude * LocationTypeFactor);
    }

    /// <summary>
    /// Checks whether the internal values of the instance represent valid GPS location.
    /// </summary>
    /// <returns>true if the object instance represents valid GPS location, false otherwise.</returns>
    public bool IsValid()
    {
      return (LatitudeMin <= Latitude) && (Latitude <= LatitudeMax) 
        && (LongitudeMin <= Longitude) && (Longitude <= LongitudeMax);
    }
  }
}
