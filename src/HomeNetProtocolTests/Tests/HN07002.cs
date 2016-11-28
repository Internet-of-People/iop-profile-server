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
  /// HN07002 - Add/Remove/Get Related Identity - Invalid Requests
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN07.md#hn07002---addremoveget-related-identity---invalid-requests
  /// </summary>
  public class HN07002 : ProtocolTest
  {
    public const string TestName = "HN07002";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }

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
      ProtocolClient issuer = new ProtocolClient();
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

        // Establish home node agreement for primary client.
        await client.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool establishHomeNodeOk = await client.EstablishHomeNodeAsync("Primary");
        client.CloseConnection();

        // Check in primary client.
        await client.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
        bool checkInOk = await client.CheckInAsync();

        // Step 1 Acceptance
        bool step1Ok = listPortsOk && establishHomeNodeOk && checkInOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");

        byte[] primaryPubKey = client.GetIdentityKeys().PublicKey;
        string type = "Card Type A";
        DateTime validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        DateTime validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        SignedRelationshipCard signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        byte[] applicationId = new byte[] { 1 };
        CardApplicationInformation cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        byte[] signature = new byte[16];
        for (int i = 0; i < signature.Length; i++)
          signature[i] = 0x40;

        Message requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        requestMessage.Request.ConversationRequest.Signature = ProtocolHelper.ByteArrayToByteString(signature);
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorInvalidSignature;

        bool req1Ok = idOk && statusOk;



        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 2 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        signature = requestMessage.Request.ConversationRequest.Signature.ToByteArray();
        signature[0] ^= 0x12;
        requestMessage.Request.ConversationRequest.Signature = ProtocolHelper.ByteArrayToByteString(signature);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidSignature;

        bool req2Ok = idOk && statusOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 3 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool req3Ok = idOk && statusOk;



        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 3 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorAlreadyExists;

        bool req4Ok = idOk && statusOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 4 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        byte[] hash = new byte[16];
        for (int i = 0; i < hash.Length; i++)
          hash[i] = 0x40;

        cardApplication.CardId = ProtocolHelper.ByteArrayToByteString(hash);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        bool detailsOk = responseMessage.Response.Details == "cardApplication.cardId";

        bool req5Ok = idOk && statusOk && detailsOk;



        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 5 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        hash = cardApplication.CardId.ToByteArray();
        hash[0] ^= 0x12;
        cardApplication.CardId = ProtocolHelper.ByteArrayToByteString(hash);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "cardApplication.cardId";

        bool req6Ok = idOk && statusOk && detailsOk;



        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "cardApplication.applicationId";

        bool req7Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[40];
        for (int i = 0; i < applicationId.Length; i++)
          applicationId[i] = 0x40;

        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "cardApplication.applicationId";

        bool req8Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        byte[] issuerPubKey = new byte[10];
        Array.Copy(signedCard.Card.IssuerPublicKey.ToByteArray(), issuerPubKey, issuerPubKey.Length);
        signedCard.Card.IssuerPublicKey = ProtocolHelper.ByteArrayToByteString(issuerPubKey);


        RelationshipCard card = new RelationshipCard()
        {
          CardId = ProtocolHelper.ByteArrayToByteString(new byte[32]),
          Version = SemVer.V100.ToByteString(),
          IssuerPublicKey = signedCard.Card.IssuerPublicKey,
          RecipientPublicKey = signedCard.Card.RecipientPublicKey,
          Type = signedCard.Card.Type,
          ValidFrom = signedCard.Card.ValidFrom,
          ValidTo = signedCard.Card.ValidTo,
        };

        byte[] cardDataToHash = card.ToByteArray();
        byte[] cardId = Crypto.Sha256(cardDataToHash);
        signedCard.Card.CardId = ProtocolHelper.ByteArrayToByteString(cardId);



        applicationId = new byte[] { 6 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.issuerSignature";

        bool req9Ok = idOk && statusOk && detailsOk;




        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        issuerPubKey = signedCard.Card.IssuerPublicKey.ToByteArray();
        issuerPubKey[0] ^= 0x12;
        signedCard.Card.IssuerPublicKey = ProtocolHelper.ByteArrayToByteString(issuerPubKey);


        card = new RelationshipCard()
        {
          CardId = ProtocolHelper.ByteArrayToByteString(new byte[32]),
          Version = SemVer.V100.ToByteString(),
          IssuerPublicKey = signedCard.Card.IssuerPublicKey,
          RecipientPublicKey = signedCard.Card.RecipientPublicKey,
          Type = signedCard.Card.Type,
          ValidFrom = signedCard.Card.ValidFrom,
          ValidTo = signedCard.Card.ValidTo,
        };

        cardDataToHash = card.ToByteArray();
        cardId = Crypto.Sha256(cardDataToHash);
        signedCard.Card.CardId = ProtocolHelper.ByteArrayToByteString(cardId);


        applicationId = new byte[] { 7 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.issuerSignature";

        bool req10Ok = idOk && statusOk && detailsOk;




        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        byte[] issuerSignature = new byte[20];
        Array.Copy(signedCard.IssuerSignature.ToByteArray(), issuerSignature, issuerSignature.Length);
        signedCard.IssuerSignature = ProtocolHelper.ByteArrayToByteString(issuerSignature);


        applicationId = new byte[] { 8 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.issuerSignature";

        bool req11Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        issuerSignature = signedCard.IssuerSignature.ToByteArray();
        issuerSignature[0] ^= 0x12;
        signedCard.IssuerSignature = ProtocolHelper.ByteArrayToByteString(issuerSignature);


        applicationId = new byte[] { 9 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.issuerSignature";

        bool req12Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        cardId = new byte[20];
        Array.Copy(signedCard.Card.CardId.ToByteArray(), cardId, cardId.Length);
        signedCard.Card.CardId = ProtocolHelper.ByteArrayToByteString(cardId);


        applicationId = new byte[] { 10 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.cardId";

        bool req13Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);
        cardId = signedCard.Card.CardId.ToByteArray();
        cardId[0] ^= 0x12;
        signedCard.Card.CardId = ProtocolHelper.ByteArrayToByteString(cardId);


        applicationId = new byte[] { 11 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.cardId";

        bool req14Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        byte[] rcptPubKey = new byte[20];
        Array.Copy(signedCard.Card.RecipientPublicKey.ToByteArray(), rcptPubKey, rcptPubKey.Length);
        signedCard = issuer.IssueRelationshipCard(rcptPubKey, type, validFrom, validTo);


        applicationId = new byte[] { 12 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.recipientPublicKey";

        bool req15Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        rcptPubKey = new byte[primaryPubKey.Length];
        Array.Copy(primaryPubKey, rcptPubKey, rcptPubKey.Length);
        rcptPubKey[0] ^= 0x12;
        signedCard = issuer.IssueRelationshipCard(rcptPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 13 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.recipientPublicKey";

        bool req16Ok = idOk && statusOk && detailsOk;



        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(1479220555000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 14 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.validFrom";

        bool req17Ok = idOk && statusOk && detailsOk;



        type = new string('a', 70);
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 15 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.type";

        bool req18Ok = idOk && statusOk && detailsOk;


        type = new string('ɐ', 35);
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 16 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.type";

        bool req19Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(new byte[] { 0, 0, 0 }, primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 17 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.version";

        bool req20Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(new byte[] { 1, 0 }, primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 18 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.version";

        bool req21Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        signedCard = issuer.IssueRelationshipCard(new byte[] { 1, 0, 0, 0 }, primaryPubKey, type, validFrom, validTo);

        applicationId = new byte[] { 19 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.version";

        bool req22Ok = idOk && statusOk && detailsOk;


        type = "Card Type A";
        validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);
        byte[] badPubKey = new byte[130];
        for (int i = 0; i < badPubKey.Length; i++)
          badPubKey[i] = 0x40;

        RelationshipCard badIssuerKeyCard = new RelationshipCard()
        {
          CardId = ProtocolHelper.ByteArrayToByteString(new byte[32]),
          Version = SemVer.V100.ToByteString(),
          IssuerPublicKey = ProtocolHelper.ByteArrayToByteString(badPubKey),
          RecipientPublicKey = ProtocolHelper.ByteArrayToByteString(primaryPubKey),
          Type = type,
          ValidFrom = ProtocolHelper.DateTimeToUnixTimestampMs(validFrom),
          ValidTo = ProtocolHelper.DateTimeToUnixTimestampMs(validTo)
        };

        signedCard = issuer.IssueRelationshipCard(badIssuerKeyCard);

        applicationId = new byte[] { 20 };
        cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

        requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "signedCard.card.issuerPublicKey";

        bool req23Ok = idOk && statusOk && detailsOk;



        // Step 2 Acceptance
        bool step2Ok = req1Ok && req2Ok && req3Ok && req4Ok && req5Ok && req6Ok && req7Ok && req8Ok && req9Ok && req10Ok
          && req11Ok && req12Ok && req13Ok && req14Ok && req15Ok && req16Ok && req17Ok && req18Ok && req19Ok && req20Ok 
          && req21Ok && req22Ok && req23Ok;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");

        applicationId = new byte[] { 21 };
        requestMessage = mb.CreateRemoveRelatedIdentityRequest(applicationId);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorNotFound;
        bool removeOk = idOk && statusOk;


        type = new string('a', 70);
        requestMessage = mb.CreateGetIdentityRelationshipsInformationRequest(client.GetIdentityId(), true, type, null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "type";

        req1Ok = idOk && statusOk && detailsOk;


        type = new string('ɐ', 35);
        requestMessage = mb.CreateGetIdentityRelationshipsInformationRequest(client.GetIdentityId(), true, type, null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "type";

        req2Ok = idOk && statusOk && detailsOk;


        // Step 3 Acceptance
        bool step3Ok = removeOk && req1Ok && req2Ok;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        Passed = step1Ok && step2Ok && step3Ok;


        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();
      issuer.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
