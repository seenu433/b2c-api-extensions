using System;

using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Linq;
using Microsoft.Graph;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace B2CAuthZ.Runtime.FuncHost
{
    public class Functions
    {
        private readonly GraphServiceClient _graphClient;
        private readonly ILogger<Functions> _log;
        public Functions(GraphServiceClient client, ILoggerFactory loggerFactory)
        {
            _graphClient = client;
            _log = loggerFactory.CreateLogger<Functions>();
        }

        [FunctionName("Enrichment")]
        public async Task<IActionResult> EnrichClaims(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "enrichment")] HttpRequest req)
        {
            var requestBody = await new System.IO.StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            string clientId = data?.client_id;
            string userObjectId = data?.objectId;
            string step = data?.step;

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(userObjectId) || string.IsNullOrEmpty(step))
            {
                return new ConflictObjectResult("Missing required parameters");
            }

            _log.LogDebug($"Received request: app {clientId}; user {userObjectId}; step {step}");
            try
            {

                var totalGroups = new List<string>();
                var groups = await _graphClient.Users[userObjectId].MemberOf.Request().GetAsync();

                while (groups.Count > 0)
                {
                    foreach (Group g in groups)
                    {
                        totalGroups.Add(g.DisplayName);
                    }
                    if (groups.NextPageRequest != null)
                    {
                        groups = await groups.NextPageRequest.GetAsync();
                    }
                    else
                    {
                        break;
                    }
                }

                return GetContinueApiResponse("GetGroups-Succeeded", "Your user groups were successfully determined.", string.Join(",", totalGroups));
            }
            catch (Exception ex)
            {
                _log.LogError($"Error: {ex.Message}");
                return GetBlockPageApiResponse("GetAppRoles-InternalError", "An error occurred while determining your groups, please try again later.");
            }
        }

        private IActionResult GetContinueApiResponse(string code, string userMessage, string groups)
        {
            return GetB2cApiConnectorResponse("Continue", code, userMessage, 200, groups);
        }

        private IActionResult GetValidationErrorApiResponse(string code, string userMessage)
        {
            return GetB2cApiConnectorResponse("ValidationError", code, userMessage, 400, null);
        }

        private IActionResult GetBlockPageApiResponse(string code, string userMessage)
        {
            return GetB2cApiConnectorResponse("ShowBlockPage", code, userMessage, 200, null);
        }

        private IActionResult GetB2cApiConnectorResponse(string action, string code, string userMessage, int statusCode, string groups)
        {
            var responseProperties = new Dictionary<string, object>
            {
                { "version", "1.0.0" },
                { "action", action },
                { "userMessage", userMessage },
                { "extension_UserRoles", groups }
            };

            if (statusCode != 200)
            {
                // Include the status in the body as well, but only for validation errors.
                responseProperties["status"] = statusCode.ToString();
            }
            return new JsonResult(responseProperties) { StatusCode = statusCode };
        }
    }
}
