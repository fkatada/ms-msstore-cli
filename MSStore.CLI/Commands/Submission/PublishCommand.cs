// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using MSStore.API;
using MSStore.CLI.Helpers;
using MSStore.CLI.Services;
using Spectre.Console;

namespace MSStore.CLI.Commands.Submission
{
    internal class PublishCommand : Command
    {
        public PublishCommand()
            : base("publish", "Starts the submission process for the existing Draft.")
        {
            AddArgument(SubmissionCommand.ProductIdArgument);
        }

        public new class Handler(ILogger<PublishCommand.Handler> logger, IStoreAPIFactory storeAPIFactory, TelemetryClient telemetryClient) : ICommandHandler
        {
            private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            private readonly IStoreAPIFactory _storeAPIFactory = storeAPIFactory ?? throw new ArgumentNullException(nameof(storeAPIFactory));
            private readonly TelemetryClient _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));

            public string ProductId { get; set; } = null!;

            public int Invoke(InvocationContext context)
            {
                return -1001;
            }

            public async Task<int> InvokeAsync(InvocationContext context)
            {
                var ct = context.GetCancellationToken();

                return await _telemetryClient.TrackCommandEventAsync<Handler>(
                    ProductId,
                    await AnsiConsole.Status().StartAsync("Publishing submission", async ctx =>
                {
                    try
                    {
                        if (ProductTypeHelper.Solve(ProductId) == ProductType.Packaged)
                        {
                            var storePackagedAPI = await _storeAPIFactory.CreatePackagedAsync(ct: ct);

                            var application = await storePackagedAPI.GetApplicationAsync(ProductId, ct);

                            if (application?.Id == null)
                            {
                                ctx.ErrorStatus($"Could not find application with ID '{ProductId}'");
                                return -1;
                            }

                            var submission = await storePackagedAPI.GetAnySubmissionAsync(application, ctx, _logger, ct);

                            if (submission?.Id == null)
                            {
                                ctx.ErrorStatus($"Could not find submission for application with ID '{ProductId}'");
                                return -1;
                            }

                            var submissionCommit = await storePackagedAPI.CommitSubmissionAsync(application.Id, submission.Id, ct);

                            if (submissionCommit == null)
                            {
                                throw new MSStoreException("Submission commit failed");
                            }

                            if (submissionCommit.Status != null)
                            {
                                ctx.SuccessStatus($"Submission Commited with status [green u]{submissionCommit.Status}[/]");
                                return 0;
                            }

                            ctx.ErrorStatus($"Could not commit submission for application with ID '{ProductId}'");
                            AnsiConsole.MarkupLine($"[red]{submissionCommit.ToErrorMessage()}[/]");

                            return -1;
                        }
                        else
                        {
                            var storeAPI = await _storeAPIFactory.CreateAsync(ct: ct);

                            var submissionId = await storeAPI.PublishSubmissionAsync(ProductId, ct);

                            if (submissionId == null)
                            {
                                return -1;
                            }

                            ctx.SuccessStatus($"Published with Id [green u]{submissionId}[/]");

                            return 0;
                        }
                    }
                    catch (Exception err)
                    {
                        _logger.LogError(err, "Error while publishing submission");
                        ctx.ErrorStatus(err);
                        return -1;
                    }
                }), ct);
            }
        }
    }
}
