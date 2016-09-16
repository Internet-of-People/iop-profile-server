using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using HomeNet.Data;

namespace HomeNet.Migrations
{
    [DbContext(typeof(Context))]
    [Migration("20160911165118_first")]
    partial class first
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.0.0-rtm-21431");

            modelBuilder.Entity("HomeNet.Data.Models.Identity", b =>
                {
                    b.Property<byte[]>("IdentityId")
                        .HasAnnotation("MaxLength", 20);

                    b.Property<string>("ExtraData")
                        .HasAnnotation("MaxLength", 200);

                    b.Property<uint>("InitialLocationEncoded");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasAnnotation("MaxLength", 64);

                    b.Property<byte[]>("Picture")
                        .HasAnnotation("MaxLength", 20480);

                    b.Property<byte[]>("PublicKey")
                        .IsRequired()
                        .HasAnnotation("MaxLength", 256);

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasAnnotation("MaxLength", 32);

                    b.Property<byte[]>("Version")
                        .IsRequired()
                        .HasAnnotation("MaxLength", 3);

                    b.HasKey("IdentityId");

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
