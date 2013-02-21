﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http.Filters;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Threading;

namespace WebApi.OutputCache
{
    public class WebApiOutputCacheAttribute : ActionFilterAttribute
    {
        // cache length in seconds
        private int _timespan = 0;

        // client cache length in seconds
        private int _clientTimeSpan = 0;

        // cache for anonymous users only?
        private bool _anonymousOnly = false;

        // cache key
        private string _cachekey = string.Empty;

        // cache repository
        private static readonly ObjectCache WebApiCache = MemoryCache.Default;

        Func<int, HttpActionContext, bool, bool> _isCachingTimeValid = (timespan, ac, anonymous) =>
        {
            if (timespan > 0)
            {
                if (anonymous)
                    if (Thread.CurrentPrincipal.Identity.IsAuthenticated)
                        return false;

                if (ac.Request.Method == HttpMethod.Get) return true;
            }

            return false;
        };

        private CacheControlHeaderValue setClientCache()
        {
            var cachecontrol = new CacheControlHeaderValue();
            cachecontrol.MaxAge = TimeSpan.FromSeconds(_clientTimeSpan);
            cachecontrol.MustRevalidate = true;
            return cachecontrol;
        }

        public WebApiOutputCacheAttribute(int timespan, int clientTimeSpan, bool anonymousOnly)
        {
            _timespan = timespan;
            _clientTimeSpan = clientTimeSpan;
            _anonymousOnly = anonymousOnly;
        }

        public override void OnActionExecuting(HttpActionContext ac)
        {
            if (ac != null)
            {
                if (_isCachingTimeValid(_timespan, ac, _anonymousOnly))
                {
                    MediaTypeWithQualityHeaderValue mediaTypeWithQualityHeaderValue = ac.Request.Headers.Accept.FirstOrDefault();
                    string acceptHeader = (mediaTypeWithQualityHeaderValue == null)
                                              ? "Default"
                                              : mediaTypeWithQualityHeaderValue.ToString();

                    _cachekey = string.Join(":", new string[] { ac.Request.RequestUri.PathAndQuery, acceptHeader });
                    
                    if (WebApiCache.Contains(_cachekey))
                    {
                        var val = WebApiCache.Get(_cachekey) as string;

                        if (val != null)
                        {
                            var contenttype = (MediaTypeHeaderValue)WebApiCache.Get(_cachekey + ":response-ct");
                            if (contenttype == null)
                                contenttype = new MediaTypeHeaderValue(_cachekey.Split(':')[1]);

                            ac.Response = ac.Request.CreateResponse();
                            ac.Response.Content = new StringContent(val);

                            ac.Response.Content.Headers.ContentType = contenttype;
                            ac.Response.Headers.CacheControl = this.setClientCache();
                            return;
                        }
                    }
                }
            }
            else
            {
                throw new ArgumentNullException("actionContext");
            }
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (!(WebApiCache.Contains(_cachekey)) && !string.IsNullOrWhiteSpace(_cachekey))
            {
                var body = actionExecutedContext.Response.Content.ReadAsStringAsync().Result;
                WebApiCache.Add(_cachekey, body, DateTime.Now.AddSeconds(_timespan));
                WebApiCache.Add(_cachekey + ":response-ct", actionExecutedContext.Response.Content.Headers.ContentType, DateTime.Now.AddSeconds(_timespan));
            }

            if (_isCachingTimeValid(_clientTimeSpan, actionExecutedContext.ActionContext, _anonymousOnly))
                actionExecutedContext.ActionContext.Response.Headers.CacheControl = setClientCache();
        }
    }
}
