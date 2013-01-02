using System;
using System.Collections.Generic;
using System.Text;

namespace Cassandra
{
    /**
     * Indicates a syntactically correct but invalid query.
     */
    public abstract class InvalidQueryException : QueryValidationException
    {
        public InvalidQueryException(string message)
            : base(message) { }

        public InvalidQueryException(string message, Exception innerException)
            : base(message, innerException) { }
    }

}
