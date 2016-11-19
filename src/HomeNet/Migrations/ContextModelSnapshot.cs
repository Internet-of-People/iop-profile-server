using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using HomeNet.Data;

namespace HomeNet.Migrations
{
    [DbContext(typeof(Context))]
    partial class ContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.1.0-preview1-22509");

            modelBuilder.Entity("HomeNet.Data.Models.HomeIdentity", b =>
                {
                    b.Property<byte[]>("IdentityId")
                        .ValueGeneratedOnAdd()
                        .HasMaxLength(32);

                    b.Property<DateTime?>("ExpirationDate");

                    b.Property<string>("ExtraData")
                        .HasMaxLength(200);

                    b.Property<byte[]>("HomeNodeId")
                        .HasMaxLength(32);

                    b.Property<decimal>("InitialLocationLatitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<decimal>("InitialLocationLongitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(64);

                    b.Property<Guid?>("ProfileImage");

                    b.Property<byte[]>("PublicKey")
                        .IsRequired()
                        .HasMaxLength(256);

                    b.Property<Guid?>("ThumbnailImage");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(64);

                    b.Property<byte[]>("Version")
                        .IsRequired()
                        .HasMaxLength(3);

                    b.HasKey("IdentityId");

                    b.HasIndex("ExpirationDate");

                    b.HasIndex("ExtraData");

                    b.HasIndex("IdentityId")
                        .IsUnique();

                    b.HasIndex("Name");

                    b.HasIndex("Type");

                    b.HasIndex("InitialLocationLatitude", "InitialLocationLongitude");

                    b.HasIndex("ExpirationDate", "InitialLocationLatitude", "InitialLocationLongitude", "Type", "Name");

                    b.ToTable("Identities");
                });

            modelBuilder.Entity("HomeNet.Data.Models.NeighborIdentity", b =>
                {
                    b.Property<byte[]>("IdentityId")
                        .ValueGeneratedOnAdd()
                        .HasMaxLength(32);

                    b.Property<DateTime?>("ExpirationDate");

                    b.Property<string>("ExtraData")
                        .HasMaxLength(200);

                    b.Property<byte[]>("HomeNodeId")
                        .HasMaxLength(32);

                    b.Property<decimal>("InitialLocationLatitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<decimal>("InitialLocationLongitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(64);

                    b.Property<Guid?>("ProfileImage");

                    b.Property<byte[]>("PublicKey")
                        .IsRequired()
                        .HasMaxLength(256);

                    b.Property<Guid?>("ThumbnailImage");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(64);

                    b.Property<byte[]>("Version")
                        .IsRequired()
                        .HasMaxLength(3);

                    b.HasKey("IdentityId");

                    b.HasIndex("ExpirationDate");

                    b.HasIndex("ExtraData");

                    b.HasIndex("HomeNodeId");

                    b.HasIndex("IdentityId")
                        .IsUnique();

                    b.HasIndex("Name");

                    b.HasIndex("Type");

                    b.HasIndex("InitialLocationLatitude", "InitialLocationLongitude");

                    b.HasIndex("InitialLocationLatitude", "InitialLocationLongitude", "Type", "Name");

                    b.ToTable("NeighborhoodIdentities");
                });

            modelBuilder.Entity("HomeNet.Data.Models.RelatedIdentity", b =>
                {
                    b.Property<byte[]>("IdentityId")
                        .HasMaxLength(32);

                    b.Property<byte[]>("ApplicationId")
                        .HasMaxLength(32);

                    b.Property<byte[]>("CardId")
                        .HasMaxLength(32);

                    b.Property<byte[]>("IssuerPublicKey")
                        .HasMaxLength(256);

                    b.Property<byte[]>("IssuerSignature")
                        .HasMaxLength(100);

                    b.Property<byte[]>("RecipientPublicKey")
                        .HasMaxLength(256);

                    b.Property<byte[]>("RecipientSignature")
                        .HasMaxLength(100);

                    b.Property<byte[]>("RelatedToIdentityId")
                        .HasMaxLength(32);

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(64);

                    b.Property<DateTime>("ValidFrom");

                    b.Property<DateTime>("ValidTo");

                    b.HasKey("IdentityId", "ApplicationId");

                    b.HasIndex("RelatedToIdentityId");

                    b.HasIndex("Type");

                    b.HasIndex("IdentityId", "ApplicationId")
                        .IsUnique();

                    b.HasIndex("ValidFrom", "ValidTo");

                    b.HasIndex("IdentityId", "Type", "RelatedToIdentityId", "ValidFrom", "ValidTo");

                    b.ToTable("RelatedIdentities");
                });

            modelBuilder.Entity("HomeNet.Data.Models.Setting", b =>
                {
                    b.Property<string>("Name")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Value")
                        .IsRequired();

                    b.HasKey("Name");

                    b.ToTable("Settings");
                });
        }
    }
}
