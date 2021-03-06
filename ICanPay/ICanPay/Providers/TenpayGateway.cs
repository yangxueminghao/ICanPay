﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Xml;

namespace ICanPay.Providers
{
    /// <summary>
    /// 财付通网关
    /// </summary>
    public sealed class TenpayGateway : GatewayBase, IPaymentUrl, IPaymentForm, IQueryNow
    {

        #region 私有字段

        private const string PayGatewayUrl = "https://gw.tenpay.com/gateway/pay.htm";
        private const string VerifyNotifyGatewayUrl = "https://gw.tenpay.com/gateway/verifynotifyid.xml";
        private const string QueryGatewayUrl = "https://gw.tenpay.com/gateway/normalorderquery.xml";

        #endregion


        #region 构造函数

        /// <summary>
        /// 初始化财付通网关
        /// </summary>
        public TenpayGateway()
        {
        }


        /// <summary>
        /// 初始化财付通网关
        /// </summary>
        /// <param name="gatewayParameterList">网关通知的数据集合</param>
        public TenpayGateway(Dictionary<string, GatewayParameter> gatewayParameterList)
            : base(gatewayParameterList)
        {
        }

        #endregion


        #region 属性

        /// <summary>
        /// 网关名称
        /// </summary>
        public override GatewayType GatewayType
        {
            get
            {
                return GatewayType.Tenpay;
            }
        }


        public override PaymentNotifyMethod PaymentNotifyMethod
        {
            get
            {
                // 通过RequestType、UserAgent来判断是否为服务器通知
                if (string.Compare(HttpContext.Current.Request.RequestType, "GET") == 0 &&
                    string.IsNullOrEmpty(HttpContext.Current.Request.UserAgent))
                {
                    return PaymentNotifyMethod.ServerNotify;
                }

                return PaymentNotifyMethod.AutoReturn;
            }
        }


        protected override Encoding PageEncoding
        {
            get
            {
                return Encoding.GetEncoding("GB2312");
            }
        }

        #endregion


        #region 方法

        /// <summary>
        /// 支付订单数据的Url
        /// </summary>
        public string BuildPaymentUrl()
        {
            InitOrderParameter();
            return string.Format("{0}?{1}", PayGatewayUrl, GetPaymentQueryString());
        }


        public string BuildPaymentForm()
        {
            InitOrderParameter();
            return GetFormHtml(PayGatewayUrl);
        }


        /// <summary>
        /// 初始化订单参数
        /// </summary>
        private void InitOrderParameter()
        {
            SetGatewayParameterValue("body", Order.Subject);
            SetGatewayParameterValue("fee_type", "1");
            SetGatewayParameterValue("notify_url", Merchant.NotifyUrl);
            SetGatewayParameterValue("out_trade_no", Order.Id);
            SetGatewayParameterValue("partner", Merchant.UserName);
            SetGatewayParameterValue("return_url", Merchant.NotifyUrl);
            SetGatewayParameterValue("spbill_create_ip", HttpContext.Current.Request.UserHostAddress);
            SetGatewayParameterValue("total_fee", Order.Amount * 100);
            SetGatewayParameterValue("input_charset", "GBK");
            SetGatewayParameterValue("sign", GetOrderSign());    // 签名需要在最后设置，以免缺少参数。
        }


        private string GetPaymentQueryString()
        {
            return BuildQueryString(GatewayParameterData);
        }


        private string GetOrderSign()
        {
            string orderSignQueryString = BuildQueryString(GetOrderSignParameter());

            return BuildQueryStringSign(orderSignQueryString);
        }


        private SortedDictionary<string, string> GetOrderSignParameter()
        {
            SortedDictionary<string, string> result = new SortedDictionary<string, string>();
            foreach (KeyValuePair<string, string> item in GetSortedGatewayParameter())
            {
                // 参数的值为空、参数名为 sign 的参数不参加签名
                if (!string.IsNullOrEmpty(item.Value) && string.Compare(item.Key, "sign") != 0)
                {
                    result.Add(item.Key, item.Value);
                }
            }

            return result;
        }


        /// <summary>
        /// 验证订单是否支付成功
        /// </summary>
        /// <remarks>这里处理查询订单的网关通知跟支付订单的网关通知</remarks>
        public override bool ValidateNotify()
        {
            if (IsSuccessResult())
            {
                ReadNotifyOrder();
                return true;
            }

            return false;
        }


        /// <summary>
        /// 是否是已成功支付的支付通知
        /// </summary>
        /// <returns></returns>
        private bool IsSuccessResult()
        {
            if (ValidateNotifyParameter())
            {
                if (ValidateNotifyId())
                {
                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// 检查支付通知，是否支付成功，货币类型是否为RMB，签名是否正确。
        /// </summary>
        /// <returns></returns>
        private bool ValidateNotifyParameter()
        {
            if (CompareGatewayParameterValue("trade_state", "0")&&
                CompareGatewayParameterValue("trade_mode", "1")&&
                CompareGatewayParameterValue("fee_type", "1")&&
                CompareGatewayParameterValue("sign", GetOrderSign()))
            {
                return true;
            }

            return false;
        }


        /// <summary>
        /// 读取通知中的订单金额、订单编号
        /// </summary>
        private void ReadNotifyOrder()
        {
            Order.Amount = GetGatewayParameterValue<double>("total_fee") * 0.01;
            Order.Id = GetGatewayParameterValue("out_trade_no");
        }



        public override void WriteSucceedFlag()
        {
            if (PaymentNotifyMethod == PaymentNotifyMethod.ServerNotify)
            {
                HttpContext.Current.Response.Write("success");
            }
        }


        /// <summary>
        /// 验证通知Id
        /// </summary>
        /// <returns></returns>
        private bool ValidateNotifyId()
        {
            string resultXml = Utility.ReadPage(GetValidateNotifyUrl(), PageEncoding);
            List<GatewayParameter> gatewayParameterData = BackupAndClearGatewayParameter(); // 需要先备份并清除之前接收到的网关的通知的数据，否者会对数据的验证造成干扰。
            ReadResultXml(resultXml);
            bool result = ValidateNotifyParameter();
            RestoreGatewayParameter(gatewayParameterData);   // 验证通知Id后还原之前的通知的数据。

            return result;
        }


        /// <summary>
        /// 验证订单金额、订单号是否与之前的通知的金额、订单号相符
        /// </summary>
        /// <returns></returns>
        private bool ValidateOrder()
        {
            if(CompareGatewayParameterValue("total_fee", Order.Amount * 100) &&
               CompareGatewayParameterValue("out_trade_no", Order.Id))
            {
                return true;
            }

            return false;
        }


        /// <summary>
        /// 获得验证通知的URL
        /// </summary>
        /// <returns></returns>

        private string GetValidateNotifyUrl()
        {
            string validateNotifyQueryString = GetValidateNotifyQueryString();

            return string.Format("{0}?{1}&sign={2}", VerifyNotifyGatewayUrl, validateNotifyQueryString, BuildQueryStringSign(validateNotifyQueryString));
        }


        /// <summary>
        /// 获得验证通知的查询字符串
        /// </summary>
        /// <returns></returns>
        private string GetValidateNotifyQueryString()
        {
            return string.Format("notify_id={0}&partner={1}", GetGatewayParameterValue("notify_id"), Merchant.UserName);
        }


        /// <summary>
        /// 读取结果的XML
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        private void ReadResultXml(string xml)
        {
            XmlDocument xmlDocument = Utility.CreateXmlSafeDocument();
            try
            {
                xmlDocument.LoadXml(xml);
            }
            catch (XmlException) { }

            foreach (XmlNode rootNode in xmlDocument.ChildNodes)
            {
                foreach (XmlNode item in rootNode.ChildNodes)
                {
                    SetGatewayParameterValue(item.Name, item.InnerText);
                }
            }
        }


        /// <summary>
        /// 备份并清除网关的参数
        /// </summary>
        private List<GatewayParameter> BackupAndClearGatewayParameter()
        {
            List<GatewayParameter> gatewayParameterData = new List<GatewayParameter>(GatewayParameterData);
            ClearAllGatewayParameter();
            return gatewayParameterData;
        }


        /// <summary>
        /// 还原网关的参数
        /// </summary>
        /// <param name="gatewayParameterData">网关的数据的集合</param>
        private void RestoreGatewayParameter(List<GatewayParameter> gatewayParameterData)
        {
            ClearAllGatewayParameter();
            foreach (GatewayParameter item in gatewayParameterData)
            {
                SetGatewayParameterValue(item.Name, item.Value, item.HttpMethod);
            }
        }


        public bool QueryNow()
        {
            ReadResultXml(Utility.ReadPage(GetQueryOrderUrl(), PageEncoding));
            if (ValidateNotifyParameter() && ValidateOrder())
            {
                return true;
            }

            return false;
        }


        /// <summary>
        /// 获得查询订单的Url
        /// </summary>
        /// <returns></returns>
        private string GetQueryOrderUrl()
        {
            string queryOrderQueryString = GetQueryOrderQueryString();

            return string.Format("{0}?{1}&sign={2}", QueryGatewayUrl, queryOrderQueryString, BuildQueryStringSign(queryOrderQueryString));
        }


        /// <summary>
        /// 获得查询订单的查询字符串
        /// </summary>
        /// <returns></returns>
        private string GetQueryOrderQueryString()
        {
            return string.Format("out_trade_no={0}&partner={1}", Order.Id, Merchant.UserName);
        }


        /// <summary>
        /// 创建查询字符串的签名
        /// </summary>
        /// <param name="queryString">查询字符串</param>
        private string BuildQueryStringSign(string queryString)
        {
            return Utility.GetMD5(GetSignQueryString(queryString), PageEncoding);    // 获得MD5值时需要使用GB2312编码，否则主题中有中文时会提示签名异常。
        }


        /// <summary>
        /// 获得用于签名的查询字符串
        /// </summary>
        /// <param name="queryString">查询字符串</param>
        private string GetSignQueryString(string queryString)
        {
            return string.Format("{0}&key={1}", queryString, Merchant.Key);
        }


        #endregion

    }
}
