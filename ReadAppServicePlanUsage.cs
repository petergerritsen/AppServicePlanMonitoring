using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace CloudDev.Azure.Monitoring
{
    public static class ReadAppServicePlanUsage
    {
        private static string key = TelemetryConfiguration.Active.InstrumentationKey = System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY", EnvironmentVariableTarget.Process);

        private static string subscriptionId = System.Environment.GetEnvironmentVariable("SUBSCRIPTIONID");

        private static string resourceGroupName = System.Environment.GetEnvironmentVariable("RESOURCEGROUP");

        private static string servicePlanName = System.Environment.GetEnvironmentVariable("APPSERVICEPLAN");

        private static TelemetryClient telemetry = new TelemetryClient() { InstrumentationKey = key };

        [FunctionName("ReadAppServicePlanUsage")]
        public static async Task Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"ReadAppServicePlanUsage triggered at: {DateTime.Now}");

            var tokenProvider = new AzureServiceTokenProvider();
            string accessToken = await tokenProvider.GetAccessTokenAsync("https://management.azure.com/");

            var restUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/serverfarms/{servicePlanName}/usages?api-version=2018-02-01";

            HttpClient _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);

            HttpResponseMessage result = await _httpClient.GetAsync(restUrl);

            if (result.IsSuccessStatusCode)
            {
                var response = await result.Content.ReadAsStringAsync();

                var o = JObject.Parse(response);

                var usage =
                (from u in o["value"]
                 where (string)u["name"]["value"] == "FileSystemStorage"
                 select u).FirstOrDefault();

                var percentageUsed = 0.0;
                if (usage != null)
                {
                    var used = (double)usage["currentValue"];
                    var limit = (double)usage["limit"];
                    percentageUsed = (used / limit) * 100;
                    telemetry.GetMetric("AppServicePlanStorageUsedPercentage", "ServicePlanName").TrackValue(percentageUsed, servicePlanName);
                    telemetry.GetMetric("AppServicePlanStorageUsed", "ServicePlanName").TrackValue(used, servicePlanName);
                    
                    log.LogInformation($"Limit: {limit}");
                    log.LogInformation($"Used: {used}");
                    log.LogInformation($"Percentage used: {percentageUsed}%");
                }
            }
            else
            {
                log.LogError($"Error calling Resource Manager REST API: {result.StatusCode}");
                throw new Exception("Error calling Resource Manager REST API");
            }

            
        }
    }
}
