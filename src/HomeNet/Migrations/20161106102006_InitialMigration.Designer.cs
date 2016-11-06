using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using HomeNet.Data;

namespace HomeNet.Migrations
{
    [DbContext(typeof(Context))]
    [Migration("20161106102006_InitialMigration")]
    partial class InitialMigration
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.1.0-preview1-22509");

            modelBuilder.Entity("HomeNet.Data.Models.Identity", b =>
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

                    b.HasIndex("IdentityId", "HomeNodeId", "Name", "Type", "InitialLocationLatitude", "InitialLocationLongitude", "ExtraData", "ExpirationDate");

                    b.ToTable("Identities");
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
