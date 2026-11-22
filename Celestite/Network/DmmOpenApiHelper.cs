using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Celestite.Network.Models;
using Celestite.Pages.Default;
using Celestite.Utils;
using Cysharp.Threading.Tasks;
using ZeroLog;

namespace Celestite.Network
{
    public class DmmOpenApiResult
    {
        private static readonly Log Logger = LogManager.GetLogger("DmmOpenApi");
        public bool Success { get; }
        public bool Failed => !Success;
        public DmmOpenApiErrorBody? Error { get; }

        protected DmmOpenApiResult(bool success, DmmOpenApiErrorBody? error = null)
        {
            switch (success)
            {
                case true when error != null:
                    throw new InvalidOperationException();
                case false when error == null:
                    throw new InvalidOperationException();
            }

            Success = success;
            Error = error;

            if (!success)
            {
                Logger.Warn(error!.Reason);
                NotificationHelper.Error(error!.Reason);
            }
        }

        protected DmmOpenApiResult(Exception error)
        {
            Success = false;
            Error = new DmmOpenApiErrorBody
            {
                Code = "E999001",
                Reason = error!.Message
            };
            /*
            Logger.Error(string.Empty, error);
            NotificationHelper.Error(error!.Message);*/
        }

        public static DmmOpenApiResult<T> Ok<T>(T value) where T : class
        {
            return new DmmOpenApiResult<T>(value, true, null);
        }

        public static DmmOpenApiResult<T> Fail<T>(DmmOpenApiErrorBody error) where T : class
        {
            return new DmmOpenApiResult<T>(null, false, error);
        }
        public static DmmOpenApiResult<T> Fail<T>(Exception error) where T : class
        {
            return new DmmOpenApiResult<T>(error);
        }
        public static DmmOpenApiResult<T> Fail<T>(string errorMessage) where T : class
        {
            return new DmmOpenApiResult<T>(false, new DmmOpenApiErrorBody
            {
                Code = "E999002",
                Reason = errorMessage
            });
        }
        public static DmmOpenApiResult Fail(DmmOpenApiErrorBody error)
        {
            return new DmmOpenApiResult(false, error);
        }
        public static DmmOpenApiResult Fail(Exception error)
        {
            return new DmmOpenApiResult(error);
        }
        public static DmmOpenApiResult Fail(string errorMessage)
        {
            return new DmmOpenApiResult(false, new DmmOpenApiErrorBody
            {
                Code = "E999002",
                Reason = errorMessage
            });
        }
    }

    public class DmmOpenApiResult<T> : DmmOpenApiResult where T : class
    {
        private readonly T? _value;
        public T Value => _value!;

        protected internal DmmOpenApiResult(T? value, bool success, DmmOpenApiErrorBody? error = null) : base(success, error)
        {
            _value = value;
        }
        protected internal DmmOpenApiResult(bool success, DmmOpenApiErrorBody? error = null) : base(success, error)
        {
        }

        protected internal DmmOpenApiResult(Exception error) : base(error)
        {
        }
    }

    public static class DmmOpenApiHelper
    {
        private const string ApiBaseUrl = "https://accounts.dmm.com";

        public static async UniTask<bool> CheckValidity(string accessToken)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                return false;
            }
            var issueResponse = await DmmGamePlayerApiHelper.CheckAccessToken(accessToken);
            if (issueResponse.Failed || !issueResponse.Value.Result)
            {
                return false;
            }
            return true;
        }

        public static async UniTask<bool> CheckValidity(string loginSecureId, string loginSessionId)
        {
            if (string.IsNullOrEmpty(loginSecureId) || string.IsNullOrEmpty(loginSessionId))
            {
                return false;
            }
            var request = new HttpRequestMessage(HttpMethod.Post, "https://apidgp-gameplayer.games.dmm.com/v5/userinfo");
            request.Headers.Add("Cookie", $"login_secure_id={loginSecureId}; login_session_id={loginSessionId}");
            var userInfoResponse = await HttpHelper.SendRawAsync(request, DmmGamePlayerApiResponseBaseContext.Default.DmmGamePlayerApiResponseUserInfoResponse);
            if (userInfoResponse.Failed || userInfoResponse.Value.ResultCode != DmmGamePlayerApiErrorCode.SUCCESS)
            {
                return false;
            }
            return true;
        }

    }
}
