using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IopCommon;
using IopProtocol;

namespace ProfileServer.Data.Models
{
    public class MissedCall
    {
        /// <summary>Class logger.</summary>
        private static Logger log = new Logger("ProfileServer.Data.Models.MissedCall");

        [Key]
        public int DbId { get; internal set; }

        [Required]
        public int CalleeId { get; set; }

        [Required]
        public byte[] CallerId { get; set; }

        [Required]
        /// <summary>Time in UTC when the message was enqueued</summary>
        public DateTime StoredAt { get; set; }

        [Required]
        [MaxLength(ProtocolHelper.MaxMessageSize)]
        public byte[] Payload { get; set; }

        public HostedIdentity Callee { get; set; }
    }
}
