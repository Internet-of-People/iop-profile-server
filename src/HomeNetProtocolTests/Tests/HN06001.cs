using Google.Protobuf;
using HomeNetCrypto;
using HomeNetProtocol;
using Iop.Homenode;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HomeNetProtocolTests.Tests
{
  /// <summary>
  /// HN06001 - Profile Search - Simple Search 1
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN06.md#hn06001---profile-search---simple-search-1
  /// </summary>
  public class HN06001 : ProtocolTest
  {
    public const string TestName = "HN06001";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Test identities profile types.</summary>
    public static List<string> ProfileTypes = new List<string>()
    {
      "Profile Type A",
      "Profile Type A",
      "Profile Type A",
      "Profile Type B",
      "Profile Type B",
      "Profile Type C",
      "Profile Type C",
    };


    /// <summary>Test identities profile names.</summary>
    public static List<string> ProfileNames = new List<string>()
    {
      "Shanghai 1",
      "Mumbai 1",
      "Karachi",
      "Buenos Aires",
      "Shanghai 2",
      "Mumbai 2",
      "Mumbai 3",
    };


    /// <summary>Test identities profile locations.</summary>
    public static List<GpsLocation> ProfileLocations = new List<GpsLocation>()
    {
      new GpsLocation(31.23m, 121.47m),
      new GpsLocation(18.96m, 72.82m),
      new GpsLocation(24.86m, 67.01m),
      new GpsLocation(-34.61m, -58.37m),
      new GpsLocation(31.231m, 121.47m),
      new GpsLocation(18.961m, 72.82m),
      new GpsLocation(18.961m, 72.82m),
    };


    /// <summary>Test identities extra data information.</summary>
    public static List<string> ProfileExtraData = new List<string>()
    {
      null,
      "t=running,Cycling,ice hockey,water polo",
      "l=Karachi,PK;a=iop://185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969;t=traveling,cycling,running",
      null,
      "running",
      "MTg1ZjhkYjMyMjcxZmUyNWY1NjFhNmZjOTM4YjJlMjY0MzA2ZWMzMDRlZGE1MTgwMDdkMTc2NDgyNjM4MTk2OQ==",
      "t=running;l=Mumbai,IN",
    };


    /// <summary>Test identities profile image file names.</summary>
    public static List<string> ProfileImages = new List<string>()
    {
      "images/HN06001.jpg",
      "images/HN06001.jpg",
      null,
      "images/HN06001.jpg",
      null,
      "images/HN06001.jpg",
      null      
    };

    /// <summary>Test identities public keys.</summary>
    public static List<byte[]> ProfilePublicKeys;

    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress NodeIp = (IPAddress)ArgumentValues["Node IP"];
      int PrimaryPort = (int)ArgumentValues["primary Port"];
      log.Trace("(NodeIp:'{0}',PrimaryPort:{1})", NodeIp, PrimaryPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        await client.ConnectAsync(NodeIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListNodePorts(rolePorts);
        client.CloseConnection();

        ProfilePublicKeys = new List<byte[]>();

        bool profileInitializationOk = true;
        for (int i = 0; i < ProfileNames.Count; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          ProfilePublicKeys.Add(profileClient.GetIdentityKeys().PublicKey);

          await profileClient.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
          bool establishHomeNodeOk = await profileClient.EstablishHomeNodeAsync(ProfileTypes[i]);
          profileClient.CloseConnection();


          await profileClient.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
          bool checkInOk = await profileClient.CheckInAsync();

          byte[] imageData = ProfileImages[i] != null ? File.ReadAllBytes(ProfileImages[i]) : null;
          bool initializeProfileOk = await profileClient.InitializeProfileAsync(ProfileNames[i], imageData, ProfileLocations[i], ProfileExtraData[i]);

          profileInitializationOk = establishHomeNodeOk && checkInOk && initializeProfileOk;
          profileClient.Dispose();

          if (!profileInitializationOk) break;
        }

        bool step1Ok = listPortsOk && profileInitializationOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();

        Message requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;


        HashSet<int> numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7 };
        bool totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        bool maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        bool profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        bool profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 2 Acceptance
        bool step2Ok = startConversationOk && idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");

        
        // Step 3
        log.Trace("Step 3");
        requestMessage = mb.CreateProfileSearchRequest("*Type B", null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 4, 5 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 3 Acceptance
        bool step3Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");


        // Step 4
        log.Trace("Step 4");
        requestMessage = mb.CreateProfileSearchRequest("Profile Type C", null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 4 Acceptance
        bool step4Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");


        // Step 5
        log.Trace("Step 5");
        requestMessage = mb.CreateProfileSearchRequest(null, "Mumbai *", null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 2, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 5 Acceptance
        bool step5Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");


        // Step 6
        log.Trace("Step 6");
        requestMessage = mb.CreateProfileSearchRequest(null, "*ai*", null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1, 2, 4, 5, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 6 Acceptance
        bool step6Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");
        requestMessage = mb.CreateProfileSearchRequest(null, null, null, new GpsLocation(18.961m, 72.82m), 10, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 7 Acceptance
        bool step7Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        // Step 8
        log.Trace("Step 8");
        requestMessage = mb.CreateProfileSearchRequest(null, null, null, new GpsLocation(18.961m, 72.82m), 5000, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 2, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 8 Acceptance
        bool step8Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");


        // Step 9
        log.Trace("Step 9");
        requestMessage = mb.CreateProfileSearchRequest(null, null, null, new GpsLocation(-12.345678m, 12.345678m), 5000, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == 0;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == 0;

        // Step 9 Acceptance
        bool step9Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;


        // Step 10
        log.Trace("Step 10");
        requestMessage = mb.CreateProfileSearchRequest(null, null, "no profiles", null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == 0;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == 0;

        // Step 10 Acceptance
        bool step10Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;

        log.Trace("Step 10: {0}", step10Ok ? "PASSED" : "FAILED");


        // Step 11
        log.Trace("Step 11");
        requestMessage = mb.CreateProfileSearchRequest(null, null, @"(^|;)t=(|[^=]+,)running([;,]|$)", null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 2, 3, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 11 Acceptance
        bool step11Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 11: {0}", step11Ok ? "PASSED" : "FAILED");

        
        // Step 12
        log.Trace("Step 12");
        requestMessage = mb.CreateProfileSearchRequest(null, null, @".+", null, 0, 2, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == 5;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 2;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == 2;

        List<IdentityNetworkProfileInformation> setA = new List<IdentityNetworkProfileInformation>(responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        bool firstPartOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;


        requestMessage = mb.CreateProfileSearchPartRequest(2, 2);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        bool recordIndexOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.RecordIndex == 2;
        bool recordCountOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.RecordCount == 2;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles.Count == 2;

        List<IdentityNetworkProfileInformation> setB = new List<IdentityNetworkProfileInformation>(responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles);

        bool secondPartOk = idOk && statusOk && recordIndexOk && recordCountOk && profilesCountOk;


        requestMessage = mb.CreateProfileSearchPartRequest(4, 1);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        recordIndexOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.RecordIndex == 4;
        recordCountOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.RecordCount == 1;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles.Count == 1;

        List<IdentityNetworkProfileInformation> setC = new List<IdentityNetworkProfileInformation>(responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles);

        bool thirdPartOk = idOk && statusOk && recordIndexOk && recordCountOk && profilesCountOk;


        requestMessage = mb.CreateProfileSearchPartRequest(0, 5);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 2, 3, 5, 6, 7 };
        recordIndexOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.RecordIndex == 0;
        recordCountOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.RecordCount == numberList.Count;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles.Count == numberList.Count;

        bool fourthPartOk = idOk && statusOk && recordIndexOk && recordCountOk && profilesCountOk;


        List<IdentityNetworkProfileInformation> setAll = new List<IdentityNetworkProfileInformation>(responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles);
        bool profileListOk1 = CheckProfileList(numberList, setAll);

        List<IdentityNetworkProfileInformation> setParts = new List<IdentityNetworkProfileInformation>(setA);
        setParts.AddRange(setB);
        setParts.AddRange(setC);
        bool profileListOk2 = CheckProfileList(numberList, setParts);


        // Step 12 Acceptance
        bool step12Ok = firstPartOk && secondPartOk && thirdPartOk && fourthPartOk && profileListOk1 && profileListOk2;

        log.Trace("Step 12: {0}", step12Ok ? "PASSED" : "FAILED");



        // Step 13
        log.Trace("Step 13");
        requestMessage = mb.CreateProfileSearchRequest(null, null, @"(^|;)t=(|[^=]+,)running([;,]|$)", null, 0, 2, 2, false, false);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == 2;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 2;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == 2;

        numberList = new HashSet<int>() { 2 };
        profileListOk1 = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles, false, true);

        numberList = new HashSet<int>() { 3 };
        profileListOk2 = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles, false, true);

        numberList = new HashSet<int>() { 7 };
        bool profileListOk3 = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles, false, true);

        profileListOk = (profileListOk1 && profileListOk2 && !profileListOk3)
          || (profileListOk1 && !profileListOk2 && profileListOk3)
          || (!profileListOk1 && profileListOk2 && profileListOk3);

        // Step 13 Acceptance
        bool step13Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 13: {0}", step13Ok ? "PASSED" : "FAILED");


        // Step 14
        log.Trace("Step 14");
        requestMessage = mb.CreateProfileSearchRequest("profile*", null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 14 Acceptance
        bool step14Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 14: {0}", step14Ok ? "PASSED" : "FAILED");


        // Step 15
        log.Trace("Step 15");
        requestMessage = mb.CreateProfileSearchRequest("*file*", null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 15 Acceptance
        bool step15Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 15: {0}", step15Ok ? "PASSED" : "FAILED");



        // Step 16
        log.Trace("Step 16");
        requestMessage = mb.CreateProfileSearchRequest("**", null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 16 Acceptance
        bool step16Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 16: {0}", step16Ok ? "PASSED" : "FAILED");



        // Step 17
        log.Trace("Step 17");
        requestMessage = mb.CreateProfileSearchRequest("*", null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 17 Acceptance
        bool step17Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 17: {0}", step17Ok ? "PASSED" : "FAILED");


        // Step 18
        log.Trace("Step 18");
        requestMessage = mb.CreateProfileSearchRequest(null, "*1", null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1, 2 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 18 Acceptance
        bool step18Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 18: {0}", step18Ok ? "PASSED" : "FAILED");


        // Step 19
        log.Trace("Step 19");
        requestMessage = mb.CreateProfileSearchRequest(null, "Shanghai 1", null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 19 Acceptance
        bool step19Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 19: {0}", step19Ok ? "PASSED" : "FAILED");



        // Step 20
        log.Trace("Step 20");
        requestMessage = mb.CreateProfileSearchRequest(null, "**", null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 20 Acceptance
        bool step20Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 20: {0}", step20Ok ? "PASSED" : "FAILED");



        // Step 21
        log.Trace("Step 21");
        requestMessage = mb.CreateProfileSearchRequest(null, "*", null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 21 Acceptance
        bool step21Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 21: {0}", step21Ok ? "PASSED" : "FAILED");


        // Step 22
        log.Trace("Step 22");
        requestMessage = mb.CreateProfileSearchRequest("*Type A", "*ai*", "water", null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 2 };
        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == numberList.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == numberList.Count;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

        // Step 22 Acceptance
        bool step22Ok = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && profileListOk;

        log.Trace("Step 22: {0}", step22Ok ? "PASSED" : "FAILED");




        // Step 23
        log.Trace("Step 23");
        requestMessage = mb.CreateProfileSearchRequest(null, null, @".+", null, 0, 2, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == 5;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 2;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == 2;

        firstPartOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;

        await Task.Delay(15000);
        requestMessage = mb.CreateProfileSearchPartRequest(8, 2);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        bool detailsOk = responseMessage.Response.Details == "recordIndex";

        secondPartOk = idOk && statusOk && detailsOk;


        await Task.Delay(15000);
        requestMessage = mb.CreateProfileSearchPartRequest(4, 5);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "recordCount";

        thirdPartOk = idOk && statusOk && detailsOk;



        await Task.Delay(22000);
        requestMessage = mb.CreateProfileSearchPartRequest(0, 500);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "recordCount";

        fourthPartOk = idOk && statusOk && detailsOk;



        requestMessage = mb.CreateProfileSearchPartRequest(0, 5);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        numberList = new HashSet<int>() { 2, 3, 5, 6, 7 };
        recordIndexOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.RecordIndex == 0;
        recordCountOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.RecordCount == numberList.Count;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles.Count == numberList.Count;

        bool fifthPartOk = idOk && statusOk && recordIndexOk && recordCountOk && profilesCountOk;

        profileListOk = CheckProfileList(numberList, responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles);


        // Step 23 Acceptance
        bool step23Ok = firstPartOk && secondPartOk && thirdPartOk && fourthPartOk && fifthPartOk && profileListOk;

        log.Trace("Step 23: {0}", step23Ok ? "PASSED" : "FAILED");



        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok && step9Ok && step10Ok 
          && step11Ok && step12Ok && step13Ok && step14Ok && step15Ok && step16Ok && step17Ok && step18Ok && step19Ok && step20Ok
          && step21Ok && step22Ok && step23Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Verifies contents of list of profiles returned by the node as a result of a search query.
    /// </summary>
    /// <param name="ProfileNumbers">Numbers of profiles that are expected to be in the profile list.</param>
    /// <param name="ProfileList">Profile list returned by the node.</param>
    /// <param name="ExactMatch">If set to true, the profile list is expected to contain only profiles specified in <paramref name="ProfileNumbers"/>.</param>
    /// <param name="NoImages">If set to true, the profile list must not contain images.</param>
    /// <returns>true if the <paramref name="ProfileList"/> contains profiles specified by profile numbers in <paramref name="ProfileNumbers"/>.</returns>
    public bool CheckProfileList(HashSet<int> ProfileNumbers, IEnumerable<IdentityNetworkProfileInformation> ProfileList, bool ExactMatch = true, bool NoImages = false)
    {
      log.Trace("()");
      bool error = false;
      bool[] profilesOk = new bool[ProfileNames.Count];
      foreach (IdentityNetworkProfileInformation profileInfo in ProfileList)
      {
        byte[] pubKey = profileInfo.IdentityPublicKey.ToByteArray();

        int profileIndex = -1;
        for (int i = 0; i < ProfilePublicKeys.Count; i++)
        {
          if (StructuralComparisons.StructuralComparer.Compare(ProfilePublicKeys[i], pubKey) == 0)
          {
            profileIndex = i;
            break;
          }
        }

        if (profileIndex != -1)
        {
          if (ProfileNumbers.Contains(profileIndex + 1))
          {
            bool piIsHostedOk = profileInfo.IsHosted == true;
            bool piIsOnlineOk = profileInfo.IsOnline == false;
            bool piTypeOk = profileInfo.Type == ProfileTypes[profileIndex];
            bool piNameOk = profileInfo.Name == ProfileNames[profileIndex];
            bool piLatitudeOk = profileInfo.Latitude == ProfileLocations[profileIndex].GetLocationTypeLatitude();
            bool piLongitudeOk = profileInfo.Longitude == ProfileLocations[profileIndex].GetLocationTypeLongitude();
            bool piExtraDataOk = (string.IsNullOrEmpty(profileInfo.ExtraData) && string.IsNullOrEmpty(ProfileExtraData[profileIndex])) || (profileInfo.ExtraData == ProfileExtraData[profileIndex]);
            bool piVersionOk = new SemVer(profileInfo.Version).Equals(SemVer.V100);

            bool piImageOk = true;
            if (NoImages) piImageOk = profileInfo.ThumbnailImage.Length == 0;
            else piImageOk = ProfileImages[profileIndex] != null ? profileInfo.ThumbnailImage.Length > 0 : profileInfo.ThumbnailImage.Length == 0;

            bool profileOk = piIsHostedOk && piIsOnlineOk && piTypeOk && piNameOk && piLatitudeOk && piLongitudeOk && piExtraDataOk && piVersionOk && piImageOk;
            if (!profileOk)
            {
              log.Trace("Profile index {0} is corrupted.", profileIndex + 1);
              error = true;
              break;
            }

            profilesOk[profileIndex] = true;
          }
          else if (ExactMatch)
          {
            log.Trace("Profile index {0} should not be on the list.", profileIndex + 1);
            error = true;
            break;
          }
        }
        else
        {
          log.Trace("Profile pub key {0} not recognized.", Crypto.ToHex(pubKey));
          error = true;
          break;
        }
      }

      foreach (int index in ProfileNumbers)
      {
        if (!profilesOk[index - 1])
        {
          log.Trace("Profile index {0} not retrieved.", index);
          error = true;
          break;
        }
      }
      
      bool res = !error;
      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
