using IopCommon;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace UnitTests
{
  /// <summary>
  /// Tests of GpsLocation class.
  /// </summary>
  public class GpsLocationTests
  {
    /// <summary>
    /// Tests GpsLocation decimal constructor.
    /// </summary>
    [Fact]
    public void ConstructorTest()
    {
      List<GpsLocation> inputs = new List<GpsLocation>()
      {
        new GpsLocation(       0.0m,         0.0m),
        new GpsLocation( 40.748456123m,  -73.9854788m),
        new GpsLocation( 36.190970456m, -115.1227017m),
        new GpsLocation( 73.507778789m,   80.53731599m),
        new GpsLocation(-23.441061159m,  144.25170398m),
        new GpsLocation( 40.757961753m,  -73.98559115m),
      };

      List<GpsLocation> expectedValues = new List<GpsLocation>()
      {
        new GpsLocation(       0.0m,         0.0m),
        new GpsLocation( 40.748456m,  -73.985478m),
        new GpsLocation( 36.190970m, -115.122701m),
        new GpsLocation( 73.507778m,   80.537315m),
        new GpsLocation(-23.441061m,  144.251703m),
        new GpsLocation( 40.757961m,  -73.985591m),
      };

      for (int i = 0; i < inputs.Count; i++)
        Assert.Equal(inputs[i], expectedValues[i]);
    }

    /// <summary>
    /// Tests DistanceBetween method series of GPS locations.
    /// </summary>
    [Fact]
    public void DistanceBetweenTest()
    {
      List<GpsLocation> inputs = new List<GpsLocation>()
      {
        new GpsLocation(       0.0m,         0.0m),    // Zero GPS coordinates, south of Ghana
        new GpsLocation( 40.748456m,  -73.985478m),    // Empire State Building, New York, US
        new GpsLocation( 36.190970m, -115.122701m),    // Hartke Park, Las Vegas, US
        new GpsLocation( 73.507778m,   80.537315m),    // Dikson Island, Russia
        new GpsLocation(-23.441061m,  144.251703m),    // Anzac Park, Longreach, Australia
        new GpsLocation( 40.757961m,  -73.985591m),    // Times Square, New York, US
      };

      List<double> expectedDistances = new List<double>()
      {
        // Zero GPS to
         8666100,  // Empire State Building
        12235660,  // Hartke Park
         9710095,  // Dikson Island
        15358880,  // Anzac Park
         8666310,  // Times Square

        // Empire State Building to 
         3583950,  // Hartke Park
         7163460,  // Dikson Island
        15975200,  // Anzac Park
            1057,  // Times Square

        // Hartke Park to 
         7759500,  // Dikson Island
        12432000,  // Anzac Park
         3583840,  // Times Square            

        // Dikson Island
        11723525,  // Anzac Park
         7162410,  // Times Square

         // Anzac Park
        15974880,  // Times Square
      };

      int index = 0;
      for (int i = 0; i < inputs.Count - 1; i++)
      {
        for (int j = i + 1; j < inputs.Count; j++)
        {
          double distance = GpsLocation.DistanceBetween(inputs[i], inputs[j]);
          Assert.True(distance.ApproxEqual(expectedDistances[index], 10));
          index++;
        }
      }
    }


    /// <summary>
    /// Tests GoVector method with series of starting locations, initial bearings and distances.
    /// </summary>
    [Fact]
    public void GoVectorTest()
    {
      List<GpsLocation> startingLocations = new List<GpsLocation>()
      {
        new GpsLocation(       0.0m,         0.0m),    // Zero GPS coordinates, south of Ghana
        new GpsLocation( 40.748456m,  175.985478m),    // North Pacific Ocean
        new GpsLocation(      89.1m, -115.122701m),    // Close to North Pole
      };

      List<double> initialBearings = new List<double>()
      {
           0.0,
          20.0,
        115.53,
      };


      List<double> inputDistances = new List<double>()
      {
              1000,
             65400,
            823456,
          15358880,
         115358880,
      };



      List<GpsLocation> expectedDestinationLocations = new List<GpsLocation>()
      {
        // Zero GPS with bearing 0.0 and distance
        new GpsLocation(  0.008991m,   0.000000m),   //       1,000 metres
        new GpsLocation(  0.588056m,   0.000000m),   //      65,400 metres
        new GpsLocation(  7.405556m,   0.000000m),   //     823,456 metres
        new GpsLocation( 41.874167m, 180.000000m),   //  15,358,880 metres
        new GpsLocation(-42.552778m,   0.000000m),   // 115,358,880 metres

        // Zero GPS with bearing 20.0 and distance
        new GpsLocation(  0.008449m,    0.003075m),  //       1,000 metres
        new GpsLocation(  0.552778m,    0.201111m),  //      65,400 metres
        new GpsLocation(  6.956667m,    2.545278m),  //     823,456 metres
        new GpsLocation( 38.846944m,  162.954444m),  //  15,358,880 metres
        new GpsLocation(-39.455833m,  -17.431389m),  // 115,358,880 metres
                                     
        // Zero GPS with bearing 115.53 and distance
        new GpsLocation( -0.003875m,    0.008113m),  //       1,000 metres
        new GpsLocation( -0.253611m,    0.530833m),  //      65,400 metres
        new GpsLocation( -3.184444m,    6.689444m),  //     823,456 metres
        new GpsLocation(-16.719167m,  141.030278m),  //  15,358,880 metres
        new GpsLocation( 16.945278m,  -39.638056m),  // 115,358,880 metres
                                     
                                     
        // North Pacific Ocean with bearing 0.0 and distance
        new GpsLocation( 40.757447m,  175.985478m),  //       1,000 metres
        new GpsLocation( 41.336667m,  175.985478m),  //      65,400 metres
        new GpsLocation( 48.153889m,  175.985478m),  //     823,456 metres
        new GpsLocation(  1.125833m,   -4.014444m),  //  15,358,880 metres
        new GpsLocation( -1.804167m,  175.985478m),  // 115,358,880 metres
                                     
        // North Pacific Ocean with bearing 20.0 and distance
        new GpsLocation( 40.756904m,  175.989537m),  //       1,000 metres
        new GpsLocation( 41.300833m,  176.253333m),  //      65,400 metres
        new GpsLocation( 47.650556m,  179.737500m),  //     823,456 metres
        new GpsLocation( -0.621667m,  -17.212222m),  //  15,358,880 metres
        new GpsLocation( -0.033611m,  162.611944m),  // 115,358,880 metres
                                     
        // North Pacific Ocean with bearing 115.53 and distance
        new GpsLocation( 40.744581m,  175.996186m),  //       1,000 metres
        new GpsLocation( 40.492778m,  176.683333m),  //      65,400 metres
        new GpsLocation( 37.243889m, -175.613333m),  //     823,456 metres
        new GpsLocation(-44.747222m,  -62.017778m),  //  15,358,880 metres
        new GpsLocation( 44.559444m,  117.065556m),  // 115,358,880 metres


        // Close to North Pole with bearing 0.0 and distance
        new GpsLocation( 89.108991m, -115.122701m),  //       1,000 metres
        new GpsLocation( 89.688056m, -115.122701m),  //      65,400 metres
        new GpsLocation( 83.494444m,   64.877222m),  //     823,456 metres
        new GpsLocation(-47.225833m,   64.877222m),  //  15,358,880 metres
        new GpsLocation( 46.547222m, -115.122701m),  // 115,358,880 metres

        // Close to North Pole with bearing 20.0 and distance
        new GpsLocation( 89.108443m, -114.925079m),  //       1,000 metres
        new GpsLocation( 89.598611m,  -85.043611m),  //      65,400 metres
        new GpsLocation( 83.433056m,   42.204722m),  //     823,456 metres
        new GpsLocation(-47.279167m,   45.212778m),  //  15,358,880 metres
        new GpsLocation( 46.600833m, -134.795000m),  // 115,358,880 metres

        // Close to North Pole with bearing 115.53 and distance
        new GpsLocation( 89.096089m, -114.608429m),  //       1,000 metres
        new GpsLocation( 88.730278m,  -90.413333m),  //      65,400 metres
        new GpsLocation( 82.164722m,  -56.567778m),  //     823,456 metres
        new GpsLocation(-48.507222m,  -49.737222m),  //  15,358,880 metres
        new GpsLocation( 47.828889m,  130.241111m),  // 115,358,880 metres

      };

      int index = 0;
      foreach (GpsLocation locFrom in startingLocations)
      {
        foreach (double bearing in initialBearings)
        {
          foreach (double distance in inputDistances)
          {
            GpsLocation locTo = locFrom.GoVector(bearing, distance);
            double latDistance = (double)(locTo.Latitude - expectedDestinationLocations[index].Latitude);
            double lonDistance = (double)(locTo.Longitude - expectedDestinationLocations[index].Longitude);
            Assert.True(latDistance.ApproxEqual(0, 0.00015));
            Assert.True(lonDistance.ApproxEqual(0, 0.00015));
            index++;
          }
        }
      }
    }


    /// <summary>
    /// Tests InitialBearingTo and FinalBearingTo methods with series of test locations.
    /// </summary>
    [Fact]
    public void BearingTest()
    {
      List<GpsLocation> inputLocations = new List<GpsLocation>()
      {
        new GpsLocation(       0.0m,         0.0m),    // Zero GPS coordinates, south of Ghana
        new GpsLocation( 40.748456m,  175.985478m),    // North Pacific Ocean
        new GpsLocation(      89.1m, -115.122701m),    // Close to North Pole
        new GpsLocation( 73.507778m,   80.537315m),    // Dikson Island, Russia 
      };

      List<double> expectedInitialBearings = new List<double>()
      {
        // Zero GPS to
          4.645278, // North Pacific Ocean
        359.185,    // Close to North Pole
         16.279722, // Dikson Island

        // North Pacific Ocean to
          1.113611, // Close to North Pole
        339.201389, // Dikson Island

        // Cose to North Pole to
        345.118889, // Dikson Island
      };


      List<double> expectedFinalBearings = new List<double>()
      {
        // Zero GPS to
        173.863333, // North Pacific Ocean
        244.88,     // Close to North Pole
         80.92,     // Dikson Island

        // North Pacific Ocean to
         69.621111, // Close to North Pole
        251.368056, // Dikson Island

        // Cose to North Pole to
        180.814167, // Dikson Island
      };


      int index = 0;
      for (int i = 0; i < inputLocations.Count - 1; i++)
      {
        GpsLocation locFrom = inputLocations[i];

        for (int j = i + 1; j < inputLocations.Count; j++)
        {
          GpsLocation locTo = inputLocations[j];

          double initialBearing = locFrom.InitialBearingTo(locTo);
          double finalBearing = locFrom.FinalBearingTo(locTo);

          Assert.True(initialBearing.ApproxEqual(expectedInitialBearings[index], 0.0003));
          Assert.True(finalBearing.ApproxEqual(expectedFinalBearings[index], 0.0003));
          index++;
        }
      }
    }



    /// <summary>
    /// Tests GetSquare method.
    /// </summary>
    [Fact]
    public void GetSquareTest()
    {
      List<GpsLocation> inputLocations = new List<GpsLocation>()
      {
        new GpsLocation(  0.0m,  0.0m),
        new GpsLocation( 86.0m, -4.0m),
      };

      List<double> inputRadiuses = new List<double>()
      {
        20000,
        300000,
      };


      List<GpsSquare> expectedSquares = new List<GpsSquare>()
      {
        new GpsSquare(
          new GpsLocation(0.18m, -0.18m),
          new GpsLocation(0.18m, 0.18m),
          new GpsLocation(-0.18m, -0.18m),
          new GpsLocation(-0.18m, 0.18m)
        ),

        new GpsSquare(
          new GpsLocation(87.004444m, -68.258056m),
          new GpsLocation(87.004444m,  60.258056m),
          new GpsLocation(82.781389m, -25.999444m),
          new GpsLocation(82.781389m,  17.999444m)
        ),
      };

      for (int i = 0; i < inputLocations.Count; i++)
      {
        GpsSquare square = inputLocations[i].GetSquare(inputRadiuses[i]);
        Assert.True(((double)square.LeftTop.Latitude).ApproxEqual((double)expectedSquares[i].LeftTop.Latitude, 0.002));
        Assert.True(((double)square.LeftTop.Longitude).ApproxEqual((double)expectedSquares[i].LeftTop.Longitude, 0.002));
        Assert.True(((double)square.RightTop.Latitude).ApproxEqual((double)expectedSquares[i].RightTop.Latitude, 0.002));
        Assert.True(((double)square.RightTop.Longitude).ApproxEqual((double)expectedSquares[i].RightTop.Longitude, 0.002));
        Assert.True(((double)square.LeftBottom.Latitude).ApproxEqual((double)expectedSquares[i].LeftBottom.Latitude, 0.002));
        Assert.True(((double)square.LeftBottom.Longitude).ApproxEqual((double)expectedSquares[i].LeftBottom.Longitude, 0.002));
        Assert.True(((double)square.RightBottom.Latitude).ApproxEqual((double)expectedSquares[i].RightBottom.Latitude, 0.002));
        Assert.True(((double)square.RightBottom.Longitude).ApproxEqual((double)expectedSquares[i].RightBottom.Longitude, 0.002));
      }
    }

  }
}
