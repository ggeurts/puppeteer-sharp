﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PuppeteerSharp
{
    /// <summary>
    /// Whenever the page sends a request, the following events are emitted by puppeteer's page:
    /// <see cref="Page.Request"/> emitted when the request is issued by the page.
    /// <see cref="Page.Response"/> emitted when/if the response is received for the request.
    /// <see cref="Page.RequestFinished"/> emitted when the response body is downloaded and the request is complete.
    /// 
    /// If request fails at some point, then instead of <see cref="Page.RequestFinished"/> event (and possibly instead of <see cref="Page.Response"/> event), the <see cref="Page.RequestFailed"/> event is emitted.
    /// 
    /// If request gets a 'redirect' response, the request is successfully finished with the <see cref="Page.RequestFinished"/> event, and a new request is issued to a redirected url.
    /// </summary>
    public class Request
    {
        #region Private Members
        private readonly CDPSession _client;
        private readonly bool _allowInterception;
        private readonly ILogger _logger;
        private bool _interceptionHandled;
        #endregion

        internal Request()
        {
        }
        internal Request(
            CDPSession client,
            string requestId,
            string interceptionId,
            bool isNavigationRequest,
            bool allowInterception,
            string url,
            ResourceType resourceType,
            Payload payload,
            Frame frame,
            List<Request> redirectChain)
        {
            _client = client;
            _allowInterception = allowInterception;
            _interceptionHandled = false;
            _logger = _client.Connection.LoggerFactory.CreateLogger<Request>();

            RequestId = requestId;
            InterceptionId = interceptionId;
            IsNavigationRequest = isNavigationRequest;
            Url = url;
            ResourceType = resourceType;
            Method = payload.Method;
            PostData = payload.PostData;
            Frame = frame;
            RedirectChainList = redirectChain;

            Headers = new Dictionary<string, object>();
            foreach (var keyValue in payload.Headers)
            {
                Headers[keyValue.Key] = keyValue.Value;
            }

            FromMemoryCache = false;
        }

        #region Properties
        /// <summary>
        /// Responsed attached to the request.
        /// </summary>
        /// <value>The response.</value>
        public Response Response { get; internal set; }
        /// <summary>
        /// Gets or sets the failure.
        /// </summary>
        /// <value>The failure.</value>
        public string Failure { get; internal set; }
        /// <summary>
        /// Gets or sets the request identifier.
        /// </summary>
        /// <value>The request identifier.</value>
        public string RequestId { get; internal set; }
        /// <summary>
        /// Gets or sets the interception identifier.
        /// </summary>
        /// <value>The interception identifier.</value>
        public string InterceptionId { get; internal set; }
        /// <summary>
        /// Gets or sets the type of the resource.
        /// </summary>
        /// <value>The type of the resource.</value>
        public ResourceType ResourceType { get; internal set; }
        /// <summary>
        /// Gets the frame.
        /// </summary>
        /// <value>The frame.</value>
        public Frame Frame { get; }
        /// <summary>
        /// Gets whether this request is driving frame's navigation
        /// </summary>
        public bool IsNavigationRequest { get; }
        /// <summary>
        /// Gets or sets the HTTP method.
        /// </summary>
        /// <value>HTTP method.</value>
        public HttpMethod Method { get; internal set; }
        /// <summary>
        /// Gets or sets the post data.
        /// </summary>
        /// <value>The post data.</value>
        public object PostData { get; internal set; }
        /// <summary>
        /// Gets or sets the HTTP headers.
        /// </summary>
        /// <value>HTTP headers.</value>
        public Dictionary<string, object> Headers { get; internal set; }
        /// <summary>
        /// Gets or sets the URL.
        /// </summary>
        /// <value>The URL.</value>
        public string Url { get; internal set; }

        /// <summary>
        /// A redirectChain is a chain of requests initiated to fetch a resource.
        /// If there are no redirects and the request was successful, the chain will be empty.
        /// If a server responds with at least a single redirect, then the chain will contain all the requests that were redirected.
        /// redirectChain is shared between all the requests of the same chain.
        /// </summary>
        /// <example>
        /// For example, if the website http://example.com has a single redirect to https://example.com, then the chain will contain one request:
        /// <code>
        /// var response = await page.GoToAsync("http://example.com");
        /// var chain = response.Request.RedirectChain;
        /// Console.WriteLine(chain.Length); // 1
        /// Console.WriteLine(chain[0].Url); // 'http://example.com'
        /// </code>
        /// If the website https://google.com has no redirects, then the chain will be empty:
        /// <code>
        /// var response = await page.GoToAsync("https://google.com");
        /// var chain = response.Request.RedirectChain;
        /// Console.WriteLine(chain.Length); // 0
        /// </code>
        /// </example>
        /// <value>The redirect chain.</value>
        public Request[] RedirectChain => RedirectChainList.ToArray();

        internal bool FromMemoryCache { get; set; }
        internal List<Request> RedirectChainList { get; }
        #endregion

        #region Public Methods

        /// <summary>
        /// Continues request with optional request overrides. To use this, request interception should be enabled with <see cref="Page.SetRequestInterceptionAsync(bool)"/>. Exception is immediately thrown if the request interception is not enabled.
        /// </summary>
        /// <param name="overrides">Optional request overwrites.</param>
        /// <returns>Task</returns>
        public async Task ContinueAsync(Payload overrides = null)
        {
            if (!_allowInterception)
            {
                throw new PuppeteerException("Request Interception is not enabled!");
            }
            if (_interceptionHandled)
            {
                throw new PuppeteerException("Request is already handled!");
            }

            _interceptionHandled = true;

            try
            {
                var requestData = new Dictionary<string, object> { ["interceptionId"] = InterceptionId };
                if (overrides?.Url != null)
                {
                    requestData["url"] = overrides.Url;
                }

                if (overrides?.Method != null)
                {
                    requestData["method"] = overrides.Method;
                }

                if (overrides?.PostData != null)
                {
                    requestData["postData"] = overrides.PostData;
                }

                if (overrides?.Headers != null)
                {
                    requestData["headers"] = overrides.Headers;
                }

                await _client.SendAsync("Network.continueInterceptedRequest", requestData).ConfigureAwait(false);
            }
            catch (PuppeteerException ex)
            {
                // In certain cases, protocol will return error if the request was already canceled
                // or the page was closed. We should tolerate these errors
                _logger.LogError(ex.ToString());
            }
        }

        /// <summary>
        /// Fulfills request with given response. To use this, request interception should be enabled with <see cref="Page.SetRequestInterceptionAsync(bool)"/>. Exception is thrown if request interception is not enabled.
        /// </summary>
        /// <param name="response">Response that will fulfill this request</param>
        /// <returns>Task</returns>
        public async Task RespondAsync(ResponseData response)
        {
            if (Url.StartsWith("data:", StringComparison.Ordinal))
            {
                return;
            }

            if (!_allowInterception)
            {
                throw new PuppeteerException("Request Interception is not enabled!");
            }
            if (_interceptionHandled)
            {
                throw new PuppeteerException("Request is already handled!");
            }

            _interceptionHandled = true;

            var responseHeaders = new Dictionary<string, object>();

            if (response.Headers != null)
            {
                foreach (var keyValue in response.Headers)
                {
                    responseHeaders[keyValue.Key] = keyValue.Value;
                }
            }

            if (response.ContentType != null)
            {
                responseHeaders["content-type"] = response.ContentType;
            }

            if (!responseHeaders.ContainsKey("content-length") && response.BodyData != null)
            {
                responseHeaders["content-length"] = response.BodyData.Length;
            }

            var statusCode = response.Status ?? HttpStatusCode.OK;
            var statusText = statusCode.ToString();
            var text = new StringBuilder();
            text.AppendLine($"HTTP/1.1 {(int)statusCode} {statusText}");

            foreach (var header in responseHeaders)
            {
                text.AppendLine($"{header.Key}: {header.Value}");
            }
            text.AppendLine(string.Empty);

            var responseData = Encoding.UTF8.GetBytes(text.ToString());

            if (response.BodyData != null)
            {
                var concatenatedData = new byte[responseData.Length + response.BodyData.Length];
                responseData.CopyTo(concatenatedData, 0);
                response.BodyData.CopyTo(concatenatedData, responseData.Length);
                responseData = concatenatedData;
            }

            try
            {
                await _client.SendAsync("Network.continueInterceptedRequest", new Dictionary<string, object>
                {
                    {"interceptionId", InterceptionId},
                    {"rawResponse", Convert.ToBase64String(responseData)}
                }).ConfigureAwait(false);
            }
            catch (PuppeteerException ex)
            {
                // In certain cases, protocol will return error if the request was already canceled
                // or the page was closed. We should tolerate these errors
                _logger.LogError(ex.ToString());
            }
        }

        /// <summary>
        /// Aborts request. To use this, request interception should be enabled with <see cref="Page.SetRequestInterceptionAsync(bool)"/>.
        /// Exception is immediately thrown if the request interception is not enabled.
        /// </summary>
        /// <param name="errorCode">Optional error code. Defaults to <see cref="RequestAbortErrorCode.Failed"/></param>
        /// <returns>Task</returns>
        public async Task AbortAsync(RequestAbortErrorCode errorCode = RequestAbortErrorCode.Failed)
        {
            if (!_allowInterception)
            {
                throw new PuppeteerException("Request Interception is not enabled!");
            }
            if (_interceptionHandled)
            {
                throw new PuppeteerException("Request is already handled!");
            }

            var errorReason = errorCode.ToString();

            _interceptionHandled = true;

            try
            {
                await _client.SendAsync("Network.continueInterceptedRequest", new Dictionary<string, object>
                {
                    {"interceptionId", InterceptionId},
                    {"errorReason", errorReason}
                }).ConfigureAwait(false);
            }
            catch (PuppeteerException ex)
            {
                // In certain cases, protocol will return error if the request was already canceled
                // or the page was closed. We should tolerate these errors
                _logger.LogError(ex.ToString());
            }
        }
        #endregion
    }
}