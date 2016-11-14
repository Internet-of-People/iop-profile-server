using HomeNet.Data.Models;
using Iop.Homenode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using HomeNetProtocol;
using HomeNet.Network;

namespace HomeNet.Data.Repositories
{
  /// <summary>
  /// Generic repository for identities, which is the base for HomeIdentityReposity for identities hosted on this node 
  /// and NeighborhoodIdentityRepository for identities hosted in this node's neighborhood.
  /// </summary>
  public class IdentityRepository<T> : GenericRepository<T> where T:BaseIdentity
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("HomeNet.Data.Repositories.IdentityRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public IdentityRepository(Context context)
      : base(context)
    {
    }

    /// <summary>
    /// Retrieves list of identities from database whose profiles match specific criteria.
    /// </summary>
    /// <param name="MaxResults">Maximum number of results to retrieve.</param>
    /// <param name="TypeFilter">Wildcard filter for identity type, or empty string if identity type filtering is not required.</param>
    /// <param name="NameFilter">Wildcard filter for profile name, or empty string if profile name filtering is not required.</param>
    /// <param name="LocationFilter">If not null, this value together with <paramref name="Radius"/> provide specification of target area, in which the identities has to have their location set. If null, GPS location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the target area radius with the centre in <paramref name="LocationFilter"/>.</param>
    /// <returns>List of identities that match the specific criteria.</returns>
    /// <remarks>On this level we query the database with an unprecise set of filters. The location filter uses GPS square instead of cirle target area 
    /// and there is no extraData filter. This means the output of this function is a superset of what we are looking for and the caller is responsible 
    /// to filter the output to get the exact set.</remarks>
    public async Task<List<T>> ProfileSearch(uint ResultOffset, uint MaxResults, string TypeFilter, string NameFilter, GpsLocation LocationFilter, uint Radius)
    {
      log.Trace("(ResultOffset:{0},MaxResults:{1},TypeFilter:'{2}',NameFilter:'{3}',LocationFilter:'{4}',Radius:{5})", ResultOffset, MaxResults, TypeFilter, NameFilter, LocationFilter, Radius);

      // First we obtain result candidates from the database. These candidates may later be filtered out by more precise use of location filter 
      // and application of the extraData filter. As we want to achieve a certain number of results, we will be working in a loop,
      // in which we query a certain number of records, then possibly filter out some of them with more precise location filter and extraData filter.
      // Then if we do not have enough results, we load more.

      IQueryable<T> query = dbSet;

      // If we are querying HomeIdentityRepository we need to make sure only active identities are counted in.
      bool homeRepo = this is HomeIdentityRepository;
      if (homeRepo)
        query = query.Where(i => i.ExpirationDate == null);

      // Apply type filter if any.
      if (!string.IsNullOrEmpty(TypeFilter) && (TypeFilter != "*") && (TypeFilter != "**"))
      {
        Expression<Func<T, bool>> typeFilterExpression = GetTypeFilterExpression(TypeFilter);
        query = query.Where(typeFilterExpression);
      }


      // Apply name filter if any.
      if (!string.IsNullOrEmpty(NameFilter) && (NameFilter != "*") && (NameFilter != "**"))
      {
        Expression<Func<T, bool>> nameFilterExpression = GetNameFilterExpression(NameFilter);
        query = query.Where(nameFilterExpression);
      }


      // Apply basic location filter if any.
      // We do not make a precise computation of whether the identity is within the target area in DB query.
      // We only present certain make limits within latitude and longitude values must be and from the results we get 
      // we then filter those that are out of the target area.
      if (LocationFilter != null)
      {
        Expression<Func<T, bool>> locationFilterExpression = GetLocationFilterExpression(LocationFilter, Radius);
        if (locationFilterExpression != null)
          query = query.Where(locationFilterExpression);
      }

      // Limit size of the result.
      if (ResultOffset > 0) query = query.Skip((int)ResultOffset).Take((int)MaxResults);
      else query = query.Take((int)MaxResults);

      // Execute query.
      List<T> res = await query.ToListAsync();

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Creates filter expression for name wildcard.
    /// </summary>
    /// <param name="NameFilter">Name wildcard filter.</param>
    /// <returns>Filter expression for the database query.</returns>
    private Expression<Func<T, bool>> GetNameFilterExpression(string NameFilter)
    {
      string nameFilter = NameFilter.ToLowerInvariant();
      Expression<Func<T, bool>> res = i => i.Name.ToLower() == nameFilter;

      // Example: NameFilter = "*abc"
      // This means that when filter STARTS with '*', we want identity name to END with "abc".
      // Note that NameFilter == "*" case is handled elsewhere.
      bool startsWith = nameFilter.EndsWith("*");
      bool endsWith = nameFilter.StartsWith("*");
      bool contains = startsWith && endsWith;

      if (contains)
      {
        nameFilter = nameFilter.Substring(1, nameFilter.Length - 2);
        res = i => i.Name.ToLower().Contains(nameFilter);
      }
      else if (startsWith)
      {
        nameFilter = nameFilter.Substring(0, nameFilter.Length - 1);
        res = i => i.Name.ToLower().StartsWith(nameFilter);
      }
      else if (endsWith)
      {
        nameFilter = nameFilter.Substring(1);
        res = i => i.Name.ToLower().EndsWith(nameFilter);
      }

      return res;
    }

    /// <summary>
    /// Creates filter expression for type wildcard.
    /// </summary>
    /// <param name="TypeFilter">Type wildcard filter.</param>
    /// <returns>Filter expression for the database query.</returns>
    private Expression<Func<T, bool>> GetTypeFilterExpression(string TypeFilter)
    {
      string typeFilter = TypeFilter.ToLowerInvariant();
      Expression<Func<T, bool>> res = i => i.Type.ToLower() == typeFilter;

      // Example: TypeFilter = "*abc"
      // This means that when filter STARTS with '*', we want identity type to END with "abc".
      // Note that TypeFilter == "*" case is handled elsewhere.
      bool startsWith = typeFilter.EndsWith("*");
      bool endsWith = typeFilter.StartsWith("*");
      bool contains = startsWith && endsWith;

      if (contains)
      {
        typeFilter = typeFilter.Substring(1, typeFilter.Length - 2);
        res = i => i.Type.ToLower().Contains(typeFilter);
      }
      else if (startsWith)
      {
        typeFilter = typeFilter.Substring(0, typeFilter.Length - 1);
        res = i => i.Type.ToLower().StartsWith(typeFilter);
      }
      else if (endsWith)
      {
        typeFilter = typeFilter.Substring(1);
        res = i => i.Type.ToLower().EndsWith(typeFilter);
      }

      return res;
    }



    /// <summary>
    /// Creates basic filter expression for location. This filter is not precise filter, 
    /// it will just filter out the majority of the identities that the caller is not interested it.
    /// </summary>
    /// <param name="LocationFilter">GPS location of the target area centre.</param>
    /// <param name="Radius">Target area radius in metres.</param>
    /// <returns>Filter expression for the database query.</returns>
    private Expression<Func<T, bool>> GetLocationFilterExpression(GpsLocation LocationFilter, uint Radius)
    {
      log.Trace("(LocationFilter:'{0:US}',Radius:{1})", LocationFilter, Radius);
      Expression<Func<T, bool>> res = null;

      // There are several separated cases:
      //  1) Radius is very large - i.e. greater than 5,000 km. In this case, we do no filtering on this level at all.
      //  2) Distance of the target area centre to one of the poles is not larger than the radius. In this case, we calculate latitude and longitude ranges,
      //     from which we then construct a target rectangle on the sphere that will represent our target area of interest.
      //  3) Distance of the target area centre to one of the poles is larger than the radius. In this case, we only set some of the boundaries,
      //     but we will not have a full rectangle on the sphere. There are several subcases here described below.


      // 1) Radius is very large, no filtering.
      if (Radius > 5000000)
      {
        log.Trace("(-)[LARGE_RADIUS]");
        return res;
      }

      GpsLocation northPole = new GpsLocation(90.0m, 0.0m);
      GpsLocation southPole = new GpsLocation(-90.0m, 0.0m);

      double northPoleDistance = LocationFilter.DistanceTo(northPole);
      double southPoleDistance = LocationFilter.DistanceTo(southPole);

      double radius = (double)Radius;
      if (radius >= northPoleDistance)
      {
        // 2) Distance to pole is not larger than the radius:
        //    a) North Pole
        //
        // In this case we go to the South from the centre to find the minimal latitude
        // and there will be no limit on longitude.
        GpsLocation minLatitudeLocation = LocationFilter.GoVector(GpsLocation.BearingSouth, radius);
        log.Trace("Radius >= North Pole Distance, min latitude is {0}.", minLatitudeLocation.Latitude);
        res = i => i.InitialLocationLatitude >= minLatitudeLocation.Latitude;
      }
      else if (radius >= southPoleDistance)
      {
        // 2) Distance to pole is not larger than the radius:
        //    b) South Pole
        //
        // In this case we go to the North from the centre to find the maximal latitude.
        // and there will be no limit on longitude.
        GpsLocation maxLatitudeLocation = LocationFilter.GoVector(GpsLocation.BearingNorth, radius);
        log.Trace("Radius >= South Pole Distance, max latitude is {0}.", maxLatitudeLocation.Latitude);
        res = i => i.InitialLocationLatitude <= maxLatitudeLocation.Latitude;
      }
      else
      {
        // 3) Distance to poles is larger than the radius.
        // 
        // In this case we create a rectangle on the sphere, in which the target identities are expected to be.
        // Using this square we will find latitude and longitude ranges for the database query.
        
        // Find a GPS square that contains the whole target circle area.
        GpsSquare square = LocationFilter.GetSquare((double)Radius);

        // Get latitude range - this is simple, left-top and right-top corners define the max latitude,
        // and left-bottom and right-bottom corners define the min latitude.
        decimal maxLatitude = square.MidTop.Latitude;
        decimal minLatitude = square.MidBottom.Latitude;
        log.Trace("GPS square is {0:US}, min latitude is {1}, max latitude is {2}.", square, minLatitude, maxLatitude);

        // Get longitude range - we have to examine all four corners here as it depends on which hemisphere they are
        // and there are several different cases due to possibility of crossing longitude 180.

        bool leftCornersSameSign = Math.Sign(square.LeftBottom.Longitude) == Math.Sign(square.LeftTop.Longitude);
        bool rightCornersSameSign = Math.Sign(square.RightBottom.Longitude) == Math.Sign(square.RightTop.Longitude);        
        
        if (leftCornersSameSign && rightCornersSameSign && (Math.Sign(square.LeftTop.Longitude) == Math.Sign(square.RightTop.Longitude)))
        {
          // a) Square does not cross longitude 180. This case is simple, we find left most and right most longitudes 
          // and our target profiles has to be between those two.
          decimal leftLongitude = Math.Min(square.LeftTop.Longitude, square.LeftBottom.Longitude);
          decimal rightLongitude = Math.Max(square.RightTop.Longitude, square.RightBottom.Longitude);

          log.Trace("Square does not cross lon 180. left longitude is {0}, right longitude is {1}.", leftLongitude, rightLongitude);
          res = i => (minLatitude <= i.InitialLocationLatitude) && (i.InitialLocationLatitude <= maxLatitude)
            && (leftLongitude <= i.InitialLocationLongitude) && (i.InitialLocationLongitude <= rightLongitude);
        }
        else
        {
          decimal leftLongitude;
          decimal rightLongitude;

          // b) Square crosses longitude 180. This is the more complicated case. One or two corners 
          // have positive longitude and the remaining have negative longitude.
          if (leftCornersSameSign)
          {
            // Left top and left bottom corners are on the same side of longitude 180.
            // The left most corner is the one with smaller longitude value.
            leftLongitude = Math.Min(square.LeftTop.Longitude, square.LeftBottom.Longitude);
          }
          else
          {
            // Left top and left bottom corners are NOT on the same side of longitude 180.
            // The left most corner is the one with the positive value as the negative value is on the right of longitude 180.
            leftLongitude = square.LeftTop.Longitude > 0 ? square.LeftTop.Longitude : square.LeftBottom.Longitude;
          }

          if (rightCornersSameSign)
          {
            // Right top and right bottom corners are on the same side of longitude 180.
            // The right most corner is the one with higher longitude value.
            rightLongitude = Math.Max(square.RightTop.Longitude, square.RightBottom.Longitude);
          }
          else
          {
            // Right top and right bottom corners are NOT on the same side of longitude 180.
            // The right most corner is the one with the negative value as the positive value is on the left of longitude 180.
            rightLongitude = square.RightTop.Longitude < 0 ? square.RightTop.Longitude : square.RightBottom.Longitude;
          }

          // Note the OR operator instead of AND operator for longitude comparison.
          // This is because a longitude value can not be higher than e.g. 170 and lower than e.g. -150 at the same time.
          // The point is within the square if its longitude is 170 or more (up to 180) OR -150 or less (down to -180).
          log.Trace("Square crosses lon 180. left longitude is {0}, right longitude is {1}.", leftLongitude, rightLongitude);
          res = i => (minLatitude <= i.InitialLocationLatitude) && (i.InitialLocationLatitude <= maxLatitude)
            && ((leftLongitude <= i.InitialLocationLongitude) || (i.InitialLocationLongitude <= rightLongitude));
        }
      }

      log.Trace("(-)");
      return res;
    }
  }
}
