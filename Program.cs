using System;
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
        public static void Main(string[] args)
        {
            Task.Run(() => DeleteAmis()).Wait();
            //Task.Run(() => DeleteOrphanSnapshots()).Wait();
        }

        private static async Task DeleteOrphanSnapshots()
        {

            var client = new AmazonEC2Client(RegionEndpoint.APSoutheast2);
            var rr = new DescribeSnapshotsRequest();
            rr.OwnerIds.Add("self");
            var snapshots = await client.DescribeSnapshotsAsync(rr);
            var request = new DescribeImagesRequest();
            request.Owners.Add("self");
            var images = await client.DescribeImagesAsync(request);
            foreach (var snapshot in snapshots.Snapshots.Where(s => !images.Images.Any(i => i.BlockDeviceMappings.Any(d => d.Ebs.SnapshotId == s.SnapshotId)) && DateTime.Today.Subtract(s.StartTime).Days > 30))
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
            request.Owners.Add("self");
            var images = await client.DescribeImagesAsync(request);
            var snapshots = await client.DescribeSnapshotsAsync();
            var launchConfigs = await asgClient.DescribeLaunchConfigurationsAsync();

            foreach (var image in images.Images.Where(x => DateTime.Parse(x.CreationDate) < new DateTime(2018, 09, 18)))
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
