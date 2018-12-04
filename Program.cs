using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon;
using Amazon.EC2.Model;
using System.Globalization;
using Amazon.AutoScaling;
using System.Linq;

namespace delete_ami
{
    public class Program
    {
        const string ownerId = "self";
        public static void Main(string[] args)
        {
            // Task.Run(DeleteAmis).Wait();
            // Task.Run(DeleteOrphanSnapshots).Wait();
            Task.Run(DeleteOrphanEBSVolumes).Wait();
        }

        private static async Task DeleteOrphanEBSVolumes()
        {
            var client = new AmazonEC2Client(RegionEndpoint.APSoutheast2);
            var describeVolumesRequest = new DescribeVolumesRequest();
            var volumes = await client.DescribeVolumesAsync(describeVolumesRequest);

            const string ebsAvailable = "available";
            foreach (var volume in volumes.Volumes.Where(x => x.Attachments.Count == 0 && x.State == ebsAvailable))
            {
                Console.WriteLine($"Deleting volume {volume.VolumeId}...");
                await client.DeleteVolumeAsync(new DeleteVolumeRequest(volume.VolumeId));
            }
        }

        private static async Task DeleteOrphanSnapshots()
        {
            var client = new AmazonEC2Client(RegionEndpoint.APSoutheast2);
            var describeSnapshotRequest = new DescribeSnapshotsRequest();
            describeSnapshotRequest.OwnerIds.Add(ownerId);

            var snapshots = await client.DescribeSnapshotsAsync(describeSnapshotRequest);
            var describeImagesRequest = new DescribeImagesRequest();
            describeImagesRequest.Owners.Add(ownerId);

            var images = await client.DescribeImagesAsync(describeImagesRequest);

            const int numberOfdaysToKeepImage = 30;
            foreach (var snapshot in snapshots.Snapshots.Where(s =>
                !images.Images.Any(i => i.BlockDeviceMappings.Any(d => d.Ebs.SnapshotId == s.SnapshotId)) &&
                DateTime.Today.Subtract(s.StartTime).Days > numberOfdaysToKeepImage))
            {
                Console.WriteLine($"Deleting snapshot {snapshot.Description}...");
                await client.DeleteSnapshotAsync(new DeleteSnapshotRequest(snapshot.SnapshotId));
            }
        }

        private static async Task DeleteAmis()
        {
            var client = new AmazonEC2Client(RegionEndpoint.APSoutheast2);
            var asgClient = new AmazonAutoScalingClient(RegionEndpoint.APSoutheast2);

            var request = new DescribeImagesRequest();
            request.Owners.Add(ownerId);
            var images = await client.DescribeImagesAsync(request);
            var launchConfigs = await asgClient.DescribeLaunchConfigurationsAsync();

            var dateToKeepAmisFrom = new DateTime(2018, 11, 03);
            foreach (var image in images.Images.Where(x => DateTime.Parse(x.CreationDate) < dateToKeepAmisFrom))
            {
                Console.WriteLine($"Deleting Image: {image.ImageId} - {image.Name}...");
                if (!image.Tags.Any(t => t.Key == "Master"))
                {
                    if (launchConfigs.LaunchConfigurations.Any(x => x.ImageId == image.ImageId))
                    {
                        Console.WriteLine("Skipped belonging to Launch configuration.");
                    }
                    else
                    {
                        await client.DeregisterImageAsync(new DeregisterImageRequest(image.ImageId));
                        var snapshotIds = image.BlockDeviceMappings.Select(x => x.Ebs.SnapshotId);
                        foreach (var snapshotId in snapshotIds)
                        {
                            Console.WriteLine($"Deleting Snapshot {snapshotId}...");
                            await client.DeleteSnapshotAsync(new DeleteSnapshotRequest(snapshotId));
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Skipping because having Tag of Master.");
                }
            }
        }
    }
}