using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetProtocol
{
  /// <summary>
  /// GPS location information and implementation of conversions between different representations.
  /// </summary>
  public class GpsLocation : IFormattable
  {
    /// <summary>Conversion factor between floating point representation and LocationType.</summary>
    public const decimal LocationTypeFactor = 1000000;

    /// <summary>Special LocationType value that represents no location.</summary>
    public const int NoLocationLocationType = 0x7FFFFFFF;

    /// <summary>Special value that represents no location.</summary>
    public static GpsLocation NoLocation = new GpsLocation(NoLocationLocationType, NoLocationLocationType);

    /// <summary>Minimal value of latitude in floating point representation.</summary>
    public const decimal LatitudeMin = -90;

    /// <summary>Maximal value of latitude in floating point representation.</summary>
    public const decimal LatitudeMax = 90;

    /// <summary>Minimal value of longitude in floating point representation.</summary>
    public const decimal LongitudeMin = -180;

    /// <summary>Maximal value of longitude in floating point representation.</summary>
    public const decimal LongitudeMax = 180;


    /// <summary>GPS bearing for North direction.</summary>
    public const double BearingNorth = 0.0;

    /// <summary>GPS bearing for North-East direction.</summary>
    public const double BearingNorthEast = 45.0;

    /// <summary>GPS bearing for East direction.</summary>
    public const double BearingEast = 90.0;

    /// <summary>GPS bearing for South-East direction.</summary>
    public const double BearingSouthEast = 135.0;

    /// <summary>GPS bearing for South direction.</summary>
    public const double BearingSouth = 180.0;

    /// <summary>GPS bearing for South-West direction.</summary>
    public const double BearingSouthWest = 225.0;

    /// <summary>GPS bearing for West direction.</summary>
    public const double BearingWest = 270.0;

    /// <summary>GPS bearing for North-West direction.</summary>
    public const double BearingNorthWest = 315.0;


    /// <summary>GPS latitude is a number in range [-90;90].</summary>
    public decimal Latitude;

    /// <summary>GPS longitude is a number in range (-180;180].</summary>
    public decimal Longitude;

    /// <summary>
    /// Initializes GPS location information from floating point values.
    /// </summary>
    /// <param name="Latitude">Floating point latitude information. The valid range of values is [-90;90].</param>
    /// <param name="Longitude">Floating point longitude information. The valid range of values is [-180;180].</param>
    public GpsLocation(decimal Latitude, decimal Longitude)
    {
      this.Latitude = Math.Truncate(Latitude * LocationTypeFactor) / LocationTypeFactor;
      this.Longitude = Math.Truncate(Longitude * LocationTypeFactor) / LocationTypeFactor;
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

    public override string ToString()
    {
      return ToString("G");
    }


    /// <summary>
    /// Formats the value of the current instance using the specified format.
    /// </summary>
    /// <param name="Format">Type of format to use. Currently only "G" and "US" is supported.</param>
    /// <returns>Formatted string.</returns>
    public string ToString(string Format)
    {
      return ToString(Format, null);
    }

    /// <summary>
    /// Formats the value of the current instance using the specified format.
    /// </summary>
    /// <param name="Format">Type of format to use. Currently only "G" and "US" is supported.</param>
    /// <param name="Provider">The provider to use to format the value.</param>
    /// <returns>Formatted string.</returns>
    public string ToString(string Format, IFormatProvider Provider)
    {
      if (string.IsNullOrEmpty(Format)) Format = "G";
      Format = Format.Trim().ToUpperInvariant();
      if (Provider == null) Provider = CultureInfo.CurrentCulture;

      string res = "N/A";
      if (IsValid())
      {
        switch (Format)
        {
          case "G":
            res = string.Format("{0} {1}", Latitude.ToString("0.######", Provider), Longitude.ToString("0.######", Provider));
            break;

          case "US":
            {
              CultureInfo enUs = new CultureInfo("en-US");
              res = string.Format("{0}, {1}", Latitude.ToString("0.######", enUs), Longitude.ToString("0.######", enUs));
              break;
            }

          default:
            res = "Invalid format";
            break;
        }
      }

      return res;
    }

    public override bool Equals(object obj)
    {
      if (!(obj is GpsLocation))
        return false;

      GpsLocation val = (GpsLocation)obj;
      return Latitude.Equals(val.Latitude) && Longitude.Equals(val.Longitude);
    }

    public override int GetHashCode()
    {
      return GetLocationTypeLatitude() ^ GetLocationTypeLongitude();
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
        && (LongitudeMin < Longitude) && (Longitude <= LongitudeMax);
    }

    /// <summary>Approximate earth radius in metres.</summary>
    public const double EarthRadius = 6371000.0;


    /// <summary>
    /// Calculates distance to another location in metres using Haversine formula.
    /// </summary>
    /// <param name="TargetLocation">Target location to which the distance is calculated.</param>
    /// <returns>Distance from this location to the target location in metres.</returns>
    /// <remarks>Formula source: http://www.movable-type.co.uk/scripts/latlong.html .</remarks>
    public double DistanceTo(GpsLocation TargetLocation)
    {
      double lat1 = (double)Latitude;
      double lon1 = (double)Longitude;
      double lat2 = (double)TargetLocation.Latitude;
      double lon2 = (double)TargetLocation.Longitude;

      // Haversine formula:
      // a = sin²(Δφ/2) + cos φ1 ⋅ cos φ2 ⋅ sin²(Δλ/2)
      // c = 2 ⋅ atan2(√a, √(1−a))
      // d = R ⋅ c
      // where φ is latitude, λ is longitude, R is earth’s radius (mean radius = 6,371 km).

      double lat1Rad = lat1.ToRadians();
      double lat2Rad = lat2.ToRadians();
      double latDiffRad = (lat2 - lat1).ToRadians();
      double lonDiffRad = (lon2 - lon1).ToRadians();

      double a = Math.Sin(latDiffRad / 2) * Math.Sin(latDiffRad / 2)
        + Math.Cos(lat1Rad) * Math.Cos(lat2Rad)
        * Math.Sin(lonDiffRad / 2) * Math.Sin(lonDiffRad / 2);

      double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

      double res = EarthRadius * c;

      return res;
    }

    /// <summary>
    /// Calculates distance between two locations.
    /// </summary>
    /// <param name="LocationA">First location.</param>
    /// <param name="LocationB">Second location.</param>
    /// <returns>Distance between the two locations in metres.</returns>
    public static double DistanceBetween(GpsLocation LocationA, GpsLocation LocationB)
    {
      return LocationA.DistanceTo(LocationB);
    }


    /// <summary>
    /// Calculates destination point given distance and bearing from start point.
    /// </summary>
    /// <param name="Bearing">Initial GPS bearing in degrees to the destination point. The valid range of values is [0, 360). See Bearing* constants.</param>
    /// <param name="Distance">Distance in metres between the start point and the destination point.</param>
    /// <returns>GPS location of the destination point.</returns>
    /// <remarks>Formula source: http://www.movable-type.co.uk/scripts/latlong.html .</remarks>
    public GpsLocation GoVector(double Bearing, double Distance)
    {
      double lat1 = (double)Latitude;
      double lon1 = (double)Longitude;
      double lat1Rad = lat1.ToRadians();
      double lon1Rad = lon1.ToRadians();
      double brnRad = Bearing.ToRadians();

      // Formula:
      // φ2 = asin(sin φ1 ⋅ cos δ + cos φ1 ⋅ sin δ ⋅ cos θ)
      // λ2 = λ1 + atan2(sin θ ⋅ sin δ ⋅ cos φ1, cos δ − sin φ1 ⋅ sin φ2)
      // where φ is latitude, λ is longitude, θ is the bearing(clockwise from north), δ is the angular distance d / R; d being the distance travelled, R the earth’s radius
      double lat2Rad = Math.Asin(Math.Sin(lat1Rad) * Math.Cos(Distance / EarthRadius)
        + Math.Cos(lat1Rad) * Math.Sin(Distance / EarthRadius) * Math.Cos(brnRad));
      double lon2Rad = lon1Rad + Math.Atan2(Math.Sin(brnRad) * Math.Sin(Distance / EarthRadius) * Math.Cos(lat1Rad),
                                      Math.Cos(Distance / EarthRadius) - Math.Sin(lat1Rad) * Math.Sin(lat2Rad));

      double lat2 = lat2Rad.ToDegrees();
      double lon2 = lon2Rad.ToDegrees();

      // Normalize longitude.
      lon2 = (lon2 + 540.0) % 360.0 - 180.0;
      if (lon2 == -180.0) lon2 = 180.0;

      GpsLocation res = new GpsLocation((decimal)lat2, (decimal)lon2);
      return res;
    }


    /// <summary>
    /// Calculates initial bearing from the start point to the destination point.
    /// </summary>
    /// <param name="Destination">Destination location.</param>
    /// <returns>Initial bearing from the start point to the destination point.</returns>
    /// <remarks>Formula source: http://www.movable-type.co.uk/scripts/latlong.html .</remarks>
    public double InitialBearingTo(GpsLocation Destination)
    {
      double lat1 = (double)Latitude;
      double lon1 = (double)Longitude;
      double lat2 = (double)Destination.Latitude;
      double lon2 = (double)Destination.Longitude;
      double lat1Rad = lat1.ToRadians();
      double lon1Rad = lon1.ToRadians();
      double lat2Rad = lat2.ToRadians();
      double lon2Rad = lon2.ToRadians();

      // Formula: 	θ = atan2(sin Δλ ⋅ cos φ2 , cos φ1 ⋅ sin φ2 − sin φ1 ⋅ cos φ2 ⋅ cos Δλ)
      // where φ1, λ1 is the start point, φ2, λ2 the end point(Δλ is the difference in longitude)

      double y = Math.Sin(lon2Rad - lon1Rad) * Math.Cos(lat2Rad);
      double x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
              Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(lon2Rad - lon1Rad);

      double res = Math.Atan2(y, x).ToDegrees();

      // Normalize.
      res = (res + 360.0) % 360.0;
      return res;
    }



    /// <summary>
    /// Calculates final bearing from the start point to the destination point.
    /// </summary>
    /// <param name="Destination">Destination location.</param>
    /// <returns>Final bearing from the start point to the destination point.</returns>
    /// <remarks>Formula source: http://www.movable-type.co.uk/scripts/latlong.html .</remarks>
    public double FinalBearingTo(GpsLocation Destination)
    {
      // For final bearing, simply take the initial bearing from the end point to the start point and reverse it (using θ = (θ+180) % 360).
      double reverseBrng = Destination.InitialBearingTo(this);

      double res = (reverseBrng + 180.0) % 360.0;
      return res;
    }


    /// <summary>
    /// Calculates GPS square from the given centre location using a radius.
    /// </summary>
    /// <param name="Radius">Half of a distance between oposite sides.</param>
    /// <returns></returns>
    public GpsSquare GetSquare(double Radius)
    {
      // We calculate positions of square mid points of top and bottom sides - i.e. points in the center of square sides.
      GpsLocation midTop = GoVector(BearingNorth, Radius);
      GpsLocation midBottom = GoVector(BearingSouth, Radius);

      // From these mid points, we navigate use West and East bearing to go to the square corners.
      GpsLocation leftTop = midTop.GoVector(BearingWest, Radius);
      GpsLocation rightTop = midTop.GoVector(BearingEast, Radius);
      GpsLocation leftBottom = midBottom.GoVector(BearingWest, Radius);
      GpsLocation rightBottom = midBottom.GoVector(BearingEast, Radius);

      GpsSquare res = new GpsSquare(leftTop, rightTop, leftBottom, rightBottom);
      return res;
    }
  }

  /// <summary>
  /// GPS square is defined by four GPS locations of its corners and holds information about mid points of top and bottom sides as well.
  /// This square is useful for a rough quick filtering of locations outside a certain radius from a starting location.
  /// </summary>
  public class GpsSquare : IFormattable
  {
    /// <summary>Location of the left-top corner of the square.</summary>
    public GpsLocation LeftTop;

    /// <summary>Location of the right-top corner of the square.</summary>
    public GpsLocation RightTop;

    /// <summary>Location of the left-bottom corner of the square.</summary>
    public GpsLocation LeftBottom;

    /// <summary>Location of the right-bottom corner of the square.</summary>
    public GpsLocation RightBottom;

    /// <summary>Location of the point in the middle of the top side of the square.</summary>
    public GpsLocation MidTop;

    /// <summary>Location of the point in the middle of the bottom side of the square.</summary>
    public GpsLocation MidBottom;

    /// <summary>
    /// Basic square constructor.
    /// </summary>
    /// <param name="LeftTop">Location of the left-top corner of the square.</param>
    /// <param name="RightTop">Location of the right-top corner of the square.</param>
    /// <param name="LeftBottom">Location of the left-bottom corner of the square.</param>
    /// <param name="RightBottom">Location of the right-bottom corner of the square.</param>
    public GpsSquare(GpsLocation LeftTop, GpsLocation RightTop, GpsLocation LeftBottom, GpsLocation RightBottom)
    {
      this.LeftTop = LeftTop;
      this.RightTop = RightTop;
      this.LeftBottom = LeftBottom;
      this.RightBottom = RightBottom;

      double bearing = LeftTop.InitialBearingTo(RightTop);
      double distance = LeftTop.DistanceTo(RightTop) / 2;
      MidTop = LeftTop.GoVector(bearing, distance);

      bearing = LeftBottom.InitialBearingTo(RightBottom);
      distance = LeftBottom.DistanceTo(RightBottom) / 2;
      MidBottom = LeftBottom.GoVector(bearing, distance);
    }


    /// <summary>
    /// Calculates GPS square from the given centre location using a radius.
    /// </summary>
    /// <param name="Radius">Half of a distance between oposite sides.</param>
    public GpsSquare(GpsLocation Centre, double Radius)
    {
      // We calculate positions of square mid points of top and bottom sides - i.e. points in the center of square sides.
      MidTop = Centre.GoVector(GpsLocation.BearingNorth, Radius);
      MidBottom = Centre.GoVector(GpsLocation.BearingSouth, Radius);

      // From these mid points, we navigate use West and East bearing to go to the square corners.
      LeftTop = MidTop.GoVector(GpsLocation.BearingWest, Radius);
      RightTop = MidTop.GoVector(GpsLocation.BearingEast, Radius);
      LeftBottom = MidBottom.GoVector(GpsLocation.BearingWest, Radius);
      RightBottom = MidBottom.GoVector(GpsLocation.BearingEast, Radius);
    }


    public override string ToString()
    {
      return ToString("G");
    }

    /// <summary>
    /// Formats the value of the current instance using the specified format.
    /// </summary>
    /// <param name="Format">Type of format to use. Currently only "G" and "US" is supported.</param>
    /// <returns>Formatted string.</returns>
    public string ToString(string Format)
    {
      return ToString(Format, null);
    }


    /// <summary>
    /// Formats the value of the current instance using the specified format.
    /// </summary>
    /// <param name="Format">Type of format to use. Currently only "G" and "US" is supported.</param>
    /// <param name="Provider">The provider to use to format the value.</param>
    /// <returns>Formatted string.</returns>
    public string ToString(string Format, IFormatProvider Provider)
    {
      return string.Format("[LT:{0}; MT:{1}, RT:{2}; LB:{3}; MB:{4}, RB:{5}]", LeftTop.ToString(Format, Provider), MidTop.ToString(Format, Provider), RightTop.ToString(Format, Provider), 
        LeftBottom.ToString(Format, Provider), MidBottom.ToString(Format, Provider), RightBottom.ToString(Format, Provider));
    }
  }
}
