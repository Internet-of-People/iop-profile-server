using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using HomeNet.Data;

namespace HomeNet.Migrations
{
    [DbContext(typeof(Context))]
    [Migration("20161102081738_sha256")]
    partial class sha256
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.0.0-rtm-21431");

            modelBuilder.Entity("HomeNet.Data.Models.Identity", b =>
                {
                    b.Property<byte[]>("IdentityId")
                        .HasAnnotation("MaxLength", 32);

                    b.Property<DateTime?>("ExpirationDate");

                    b.Property<string>("ExtraData")
                        .HasAnnotation("MaxLength", 200);

                    b.Property<byte[]>("HomeNodeId")
                        .HasAnnotation("MaxLength", 32);

                    b.Property<uint>("InitialLocationEncoded");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasAnnotation("MaxLength", 64);

                    b.Property<Guid?>("ProfileImage");

                    b.Property<byte[]>("PublicKey")
                        .IsRequired()
                        .HasAnnotation("MaxLength", 256);

                    b.Property<Guid?>("ThumbnailImage");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasAnnotation("MaxLength", 64);

                    b.Property<byte[]>("Version")
                        .IsRequired()
                        .HasAnnotation("MaxLength", 3);

                    b.HasKey("IdentityId");

                    b.HasIndex("IdentityId", "HomeNodeId", "Name", "Type", "ExtraData", "ExpirationDate");

                    b.ToTable("Identities");
                });

            modelBuilder.Entity("HomeNet.Data.Models.Setting", b =>
                {
                    b.Property<string>("Name");

                    b.Property<string>("Value")
                        .IsRequired();

                    b.HasKey("Name");

                    b.ToTable("Settings");
                });
        }
    }
}
