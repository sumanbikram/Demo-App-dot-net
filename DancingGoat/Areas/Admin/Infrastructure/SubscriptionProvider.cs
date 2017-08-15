﻿using System;
using System.Collections.Generic;
using System.Linq;

using System.Net.Http;
using System.Threading.Tasks;
using DancingGoat.Areas.Admin.Models;

namespace DancingGoat.Areas.Admin.Infrastructure
{
    public class SubscriptionProvider : ProviderBase
    {
        private readonly ProjectProvider _projectProvider;

        public SubscriptionProvider(HttpClient httpClient, ProjectProvider projectProvider) : base(httpClient)
        {
            _projectProvider = projectProvider ?? throw new ArgumentNullException(nameof(projectProvider));
        }

        public async Task<SubscriptionStatusResults> GetSubscriptionsStatusAsync(string token, IEnumerable<SubscriptionModel> subscriptions = null, IEnumerable<ProjectModel> projects = null)
        {
            var activeProjects = projects?.Where(p => p.Inactive == false) ?? await _projectProvider.GetProjectsAsync(token, true);
            var allSubscriptions = subscriptions ?? await GetSubscriptionsAsync(token);
            var activeSubscriptions = allSubscriptions?.Where(s => s.Status.Equals(STATUS_ACTIVE, StringComparison.InvariantCultureIgnoreCase));

            // Inactive expired subscriptions. 
            if (!activeSubscriptions.Any() && !allSubscriptions.All(s => s.EndAt > DateTime.Now))
            {
                var latestExpiredSubscriptions = allSubscriptions.OrderByDescending(s => s.EndAt);

                // Will allow to either convert to the free plan or to use the shared project.
                if (!activeProjects.Any())
                {
                    return new SubscriptionStatusResults
                    {
                        Status = SubscriptionStatus.Expired,
                        EndAt = latestExpiredSubscriptions.FirstOrDefault().EndAt.Value,
                        Projects = null
                    };
                }

                // Will allow to either convert to the free plan, to use the shared project, or to pick among active projects.
                else
                {
                    return new SubscriptionStatusResults
                    {
                        Status = SubscriptionStatus.Expired,
                        EndAt = latestExpiredSubscriptions.FirstOrDefault().EndAt.Value,
                        Projects = activeProjects
                    };
                }
            }

            // Inactive non-expired subscriptions.
            else if (!activeSubscriptions.Any() && allSubscriptions.Any(s => s.EndAt <= DateTime.Now))
            {
                // Will suggest activating subscriptions (manually) and continue.
                if (!activeProjects.Any())
                {
                    return new SubscriptionStatusResults
                    {
                        Status = SubscriptionStatus.Inactive,
                        Projects = null
                    };
                }

                // Will suggest activating subscriptions (manually) or picking among active projects.
                else
                {
                    return new SubscriptionStatusResults
                    {
                        Status = SubscriptionStatus.Inactive,
                        Projects = activeProjects
                    };
                }
            }

            return new SubscriptionStatusResults
            {
                Status = SubscriptionStatus.Active,
                Projects = activeProjects
            };
        }

        public async Task<IEnumerable<SubscriptionModel>> GetSubscriptionsAsync(string token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"{KC_BASE_URL}user/subscriptions"))
            {
                using (HttpResponseMessage response = await GetResponseAsync(token, request))
                {
                    return await GetResultAsync<IEnumerable<SubscriptionModel>>(response);
                }
            }
        }

        public async Task<Tuple<SubscriptionModel, ProjectModel>> StartTrialAndSampleAsync(string token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{KC_BASE_URL}subscription/trial"))
            {
                using (HttpResponseMessage response = await GetResponseAsync(token, request))
                {
                    var subscription = await GetResultAsync<SubscriptionModel>(response);

                    if (subscription != null)
                    {
                        var project = await _projectProvider.DeploySampleAsync(token, subscription.SubscriptionId);

                        return new Tuple<SubscriptionModel, ProjectModel>(subscription, project);
                    }
                    else
                    {
                        return new Tuple<SubscriptionModel, ProjectModel>(null, null);
                    }
                }
            }
        }

        public async Task<SubscriptionModel> ConvertToFreeAsync(string token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{KC_BASE_URL}subscription/free"))
            {
                using (HttpResponseMessage response = await GetResponseAsync(token, request))
                {
                    var subscription = await GetResultAsync<SubscriptionModel>(response);

                    if (subscription != null && subscription.PlanName == "free")
                    {
                        AppSettingProvider.SubscriptionExpiresAt = DateTime.MaxValue;

                        return subscription;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }
    }
}