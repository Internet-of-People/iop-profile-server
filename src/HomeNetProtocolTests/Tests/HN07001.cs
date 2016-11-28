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
  /// HN07001 - Add/Remove/Get Related Identity
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn07001---addremoveget-related-identity
  /// </summary>
  public class HN07001 : ProtocolTest
  {
    public const string TestName = "HN07001";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }

    /// <summary>Card issuing clients.</summary>
    public static List<ProtocolClient> CardIssuers;

    /// <summary>Number of identities for issuing cards.</summary>
    public const int IssuerCount = 5;

    /// <summary>List of issued relationship cards.</summary>
    public static List<SignedRelationshipCard> SignedCards;

    /// <summary>List of card applications.</summary>
    public static List<CardApplicationInformation> CardApplications;


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

      ProtocolClient clientPrimary = new ProtocolClient();
      ProtocolClient clientSecondary = new ProtocolClient();
      try
      {
        MessageBuilder mbPrimary = clientPrimary.MessageBuilder;
        MessageBuilder mbSecondary = clientSecondary.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
    
        // Get port list.
        await clientPrimary.ConnectAsync(NodeIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await clientPrimary.ListNodePorts(rolePorts);
        clientPrimary.CloseConnection();

        // Establish home node agreement for primary client.
        await clientPrimary.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool establishHomeNodeOk = await clientPrimary.EstablishHomeNodeAsync("Primary");
        clientPrimary.CloseConnection();

        // Check in primary client.
        await clientPrimary.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
        bool checkInOk = await clientPrimary.CheckInAsync();

        bool primaryOk = establishHomeNodeOk && checkInOk;

        // Establish home node agreement for secondary client.
        await clientSecondary.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        establishHomeNodeOk = await clientSecondary.EstablishHomeNodeAsync("Primary");
        clientSecondary.CloseConnection();

        // Check in secondary client.
        await clientSecondary.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
        checkInOk = await clientSecondary.CheckInAsync();

        bool secondaryOk = establishHomeNodeOk && checkInOk;

        // Create card issuers.
        CardIssuers = new List<ProtocolClient>();
        for (int i = 0; i < IssuerCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          CardIssuers.Add(profileClient);
        }


        // Step 1 Acceptance
        bool step1Ok = listPortsOk && primaryOk && secondaryOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");

        SignedCards = new List<SignedRelationshipCard>();
        CardApplications = new List<CardApplicationInformation>();

        // Just to make it easy to follow the test specification.
        ProtocolClient Identity1 = CardIssuers[0];
        ProtocolClient Identity2 = CardIssuers[1];
        ProtocolClient Identity3 = CardIssuers[2];
        ProtocolClient Identity4 = CardIssuers[3];
        ProtocolClient Identity5 = CardIssuers[4];

        log.Trace("Identity1 ID: {0}", Crypto.ToHex(Identity1.GetIdentityId()));
        log.Trace("Identity2 ID: {0}", Crypto.ToHex(Identity2.GetIdentityId()));
        log.Trace("Identity3 ID: {0}", Crypto.ToHex(Identity3.GetIdentityId()));
        log.Trace("Identity4 ID: {0}", Crypto.ToHex(Identity4.GetIdentityId()));
        log.Trace("Identity5 ID: {0}", Crypto.ToHex(Identity5.GetIdentityId()));


        byte[] primaryPubKey = clientPrimary.GetIdentityKeys().PublicKey;
        string type = "Card Type A";
        DateTime validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        DateTime validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        SignedRelationshipCard signedCard = Identity1.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        byte[] applicationId = new byte[] { 1 };
        CardApplicationInformation cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        Message requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        Message responseMessage = await clientPrimary.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;

        bool req1Ok = idOk && statusOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(1479220557000);
        signedCard = Identity1.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 2 };
        cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req2Ok = idOk && statusOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = Identity2.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 3 };
        cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req3Ok = idOk && statusOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(2479220555000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = Identity2.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 4 };
        cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req4Ok = idOk && statusOk;


        type = "Card Type B";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = Identity3.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 5 };
        cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req5Ok = idOk && statusOk;


        type = "Card Type B";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = Identity4.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 6 };
        cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req6Ok = idOk && statusOk;


        type = "Card Type C";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = Identity4.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 7 };
        cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req7Ok = idOk && statusOk;


        type = "Card Type C";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = Identity4.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 8 };
        cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req8Ok = idOk && statusOk;


        type = "Other";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = Identity5.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 9 };
        cardApplication = clientPrimary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbPrimary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req9Ok = idOk && statusOk;


        // Step 2 Acceptance
        bool step2Ok = req1Ok && req2Ok && req3Ok && req4Ok && req5Ok && req6Ok && req7Ok && req8Ok && req9Ok;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");

        
        // Step 3
        log.Trace("Step 3");

        byte[] secondaryPubKey = clientSecondary.GetIdentityKeys().PublicKey;
        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = Identity1.IssueRelationshipCard(secondaryPubKey, type, validFrom, validTo);
        SignedCards.Add(signedCard);

        applicationId = new byte[] { 1 };
        cardApplication = clientSecondary.CreateRelationshipCardApplication(applicationId, signedCard);
        CardApplications.Add(cardApplication);

        requestMessage = mbSecondary.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req10Ok = idOk && statusOk;


        // Step 3 Acceptance
        bool step3Ok = req10Ok;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");


        // Step 4
        log.Trace("Step 4");

        byte[] primaryClientId = clientPrimary.GetIdentityId();
        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, null, null);
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        byte[] receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        bool versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        HashSet<int> numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        bool relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req1Ok = idOk && statusOk && versionOk && relationshipsOk;


        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, false, null, null);
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 1, 3, 5, 6, 7, 8, 9 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req2Ok = idOk && statusOk && versionOk && relationshipsOk;


        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, "*", null);
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req3Ok = idOk && statusOk && versionOk && relationshipsOk;


        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, "**", null);
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req4Ok = idOk && statusOk && versionOk && relationshipsOk;


        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, "Card*", null);
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7, 8 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req5Ok = idOk && statusOk && versionOk && relationshipsOk;


        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, "*Type A", null);
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 1, 2, 3, 4 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req6Ok = idOk && statusOk && versionOk && relationshipsOk;


        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, "*Type *", null);
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 1, 2, 3, 4, 5, 6, 7, 8 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req7Ok = idOk && statusOk && versionOk && relationshipsOk;


        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, null, Identity1.GetIdentityId());
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 1, 2 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req8Ok = idOk && statusOk && versionOk && relationshipsOk;


        requestMessage = mbSecondary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, "*C", Identity4.GetIdentityId());
        await clientSecondary.SendMessageAsync(requestMessage);
        responseMessage = await clientSecondary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 7, 8 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req9Ok = idOk && statusOk && versionOk && relationshipsOk;


        // Step 4 Acceptance
        bool step4Ok = req1Ok && req2Ok && req3Ok && req4Ok && req5Ok && req6Ok && req7Ok && req8Ok && req9Ok;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");



        // Step 5
        log.Trace("Step 5");

        applicationId = new byte[] { 2 };
        requestMessage = mbPrimary.CreateRemoveRelatedIdentityRequest(applicationId);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool removeRelation1Ok = idOk && statusOk;

        applicationId = new byte[] { 4 };
        requestMessage = mbPrimary.CreateRemoveRelatedIdentityRequest(applicationId);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool removeRelation2Ok = idOk && statusOk;



        requestMessage = mbPrimary.CreateGetIdentityRelationshipsInformationRequest(primaryClientId, true, "*Type a*", null);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        numberList = new HashSet<int>() { 1, 3 };
        relationshipsOk = CheckRelationships(numberList, responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships);

        req1Ok = idOk && statusOk && versionOk && relationshipsOk;



        byte[] partId = new byte[10];
        Array.Copy(primaryClientId, partId, partId.Length);
        requestMessage = mbPrimary.CreateGetIdentityRelationshipsInformationRequest(partId, true, "*Type a*", null);
        await clientPrimary.SendMessageAsync(requestMessage);
        responseMessage = await clientPrimary.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        receivedVersion = responseMessage.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        relationshipsOk = responseMessage.Response.SingleResponse.GetIdentityRelationshipsInformation.Relationships.Count == 0;

        req1Ok = idOk && statusOk && versionOk && relationshipsOk;



        // Step 5 Acceptance
        bool step5Ok = removeRelation1Ok && removeRelation2Ok && req1Ok && req2Ok;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      clientPrimary.Dispose();
      clientSecondary.Dispose();

      if (CardIssuers != null)
      {
        for (int i = 0; i < IssuerCount; i++)
        {
          if (CardIssuers[i] != null)
            CardIssuers[i].Dispose();
        }
      }


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Verifies contents of list of relationships returned by the node against the expected list of cards.
    /// </summary>
    /// <param name="CardNumbers">Numbers of cards that are expected to be in the relationship list.</param>
    /// <param name="RelationshipList">Card list returned by the node.</param>
    /// <returns>true if the <paramref name="RelationshipList"/> contains cards specified by card numbers in <paramref name="CardNumbers"/>.</returns>
    public bool CheckRelationships(HashSet<int> CardNumbers, IEnumerable<IdentityRelationship> RelationshipList)
    {
      log.Trace("()");
      bool error = false;
      bool[] cardsOk = new bool[SignedCards.Count];
      foreach (IdentityRelationship relationship in RelationshipList)
      {
        CardApplicationInformation cardApplication = relationship.CardApplication;
        byte[] cardApplicationSignature = relationship.CardApplicationSignature.ToByteArray();
        SignedRelationshipCard signedCard = relationship.Card;
        RelationshipCard card = signedCard.Card;
        byte[] cardId = card.CardId.ToByteArray();

        int cardIndex = -1;
        for (int i = 0; i < SignedCards.Count; i++)
        {
          byte[] existingCardId = SignedCards[i].Card.CardId.ToByteArray();
          if (!cardsOk[i] && (StructuralComparisons.StructuralComparer.Compare(existingCardId, cardId) == 0))
          {
            cardIndex = i;
            break;
          }
        }

        if (cardIndex != -1)
        {
          if (CardNumbers.Contains(cardIndex + 1))
          {
            byte[] issuerPublicKey = card.IssuerPublicKey.ToByteArray();
            byte[] cardSignature = signedCard.IssuerSignature.ToByteArray();

            bool cardSignatureOk = Ed25519.Verify(cardSignature, cardId, issuerPublicKey);
            bool cardContentOk = StructuralComparisons.StructuralComparer.Compare(SignedCards[cardIndex].ToByteArray(), signedCard.ToByteArray()) == 0;

            bool cardOk = cardSignatureOk && cardContentOk;

            byte[] recipientPublicKey = card.RecipientPublicKey.ToByteArray();
            bool applicationSignatureOk = Ed25519.Verify(cardApplicationSignature, cardApplication.ToByteArray(), recipientPublicKey);
            bool applicationContentOk = StructuralComparisons.StructuralComparer.Compare(CardApplications[cardIndex].ToByteArray(), cardApplication.ToByteArray()) == 0;
            bool applicationOk = applicationSignatureOk && applicationContentOk;

            if (!cardOk)
            {
              log.Trace("Card index {0} is corrupted.", cardIndex + 1);
              error = true;
              break;
            }

            if (!applicationOk)
            {
              log.Trace("Card application ID '{0}' for card index {1} is corrupted.", Crypto.ToHex(cardApplication.ApplicationId.ToByteArray()), cardIndex + 1);
              error = true;
              break;
            }

            cardsOk[cardIndex] = true;
          }
        }
        else
        {
          log.Trace("Card ID '{0}' not recognized.", Crypto.ToHex(cardId));
          error = true;
          break;
        }
      }

      foreach (int index in CardNumbers)
      {
        if (!cardsOk[index - 1])
        {
          log.Trace("Card index {0} not retrieved.", index);
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
