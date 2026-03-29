using System;

namespace XtsApiClient
{
    /// <summary>
    /// Base exception class representing an XTS client exception.
    /// Every specific XTS client exception is a subclass of this
    /// and exposes two properties: Code (HTTP error code) and Message (error text).
    /// </summary>
    public class XtsException : Exception
    {
        public int Code { get; }

        public XtsException(string message, int code = 500)
            : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// An unclassified, general error. Default code is 500.
    /// </summary>
    public class XtsGeneralException : XtsException
    {
        public XtsGeneralException(string message, int code = 500)
            : base(message, code) { }
    }

    /// <summary>
    /// Represents all token and authentication related errors. Default code is 400.
    /// </summary>
    public class XtsTokenException : XtsException
    {
        public XtsTokenException(string message, int code = 400)
            : base(message, code) { }
    }

    /// <summary>
    /// Represents permission denied exceptions for certain calls. Default code is 400.
    /// </summary>
    public class XtsPermissionException : XtsException
    {
        public XtsPermissionException(string message, int code = 400)
            : base(message, code) { }
    }

    /// <summary>
    /// Represents all order placement and manipulation errors. Default code is 400.
    /// </summary>
    public class XtsOrderException : XtsException
    {
        public XtsOrderException(string message, int code = 400)
            : base(message, code) { }
    }

    /// <summary>
    /// Represents user input errors such as missing and invalid parameters. Default code is 400.
    /// </summary>
    public class XtsInputException : XtsException
    {
        public XtsInputException(string message, int code = 400)
            : base(message, code) { }
    }

    /// <summary>
    /// Represents a bad response from the backend Order Management System (OMS). Default code is 500.
    /// </summary>
    public class XtsDataException : XtsException
    {
        public XtsDataException(string message, int code = 500)
            : base(message, code) { }
    }

    /// <summary>
    /// Represents a network issue between XTS and the backend Order Management System (OMS). Default code is 500.
    /// </summary>
    public class XtsNetworkException : XtsException
    {
        public XtsNetworkException(string message, int code = 500)
            : base(message, code) { }
    }
}
