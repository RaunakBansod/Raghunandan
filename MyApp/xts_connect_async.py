import json
import logging
import traceback
from typing import List, Dict, Any

#import httpx as requests
from httpx import AsyncClient as requests
from urllib.parse import urljoin
from datetime import datetime
import pytz
from httpx import PoolTimeout, ConnectTimeout, ReadTimeout, ConnectError
from . import xts_exception as ex
import asyncio
log = logging.getLogger(__name__)


class XTSCommon:
    """Base variables class"""
    def __init__(self, token=None, userID=None, isInvestorClient=None):

        self.token = token
        self.userID = userID
        self.isInvestorClient = isInvestorClient


class XTSConnect(XTSCommon):
    """The XTS Connect API wrapper class."""
    # Constants
    # Products
    PRODUCT_MIS = "MIS"
    PRODUCT_NRML = "NRML"
    PRODUCT_CNC = "CNC"

    # Order types
    ORDER_TYPE_MARKET = "MARKET"
    ORDER_TYPE_LIMIT = "LIMIT"
    ORDER_TYPE_STOPMARKET = "STOPMARKET"
    ORDER_TYPE_STOPLIMIT = "STOPLIMIT"

    # Transaction type
    TRANSACTION_TYPE_BUY = "BUY"
    TRANSACTION_TYPE_SELL = "SELL"

    # Squareoff mode
    SQUAREOFF_DAYWISE = "DayWise"
    SQUAREOFF_NETWISE = "Netwise"

    # Squareoff position quantity types
    SQUAREOFFQUANTITY_EXACTQUANTITY = "ExactQty"
    SQUAREOFFQUANTITY_PERCENTAGE = "Percentage"

    # Validity
    TimeinForce_GTC = "GTC"
    TimeinForce_IOC = "IOC"
    TimeinForce_FOK = "FOK"
    TimeinForce_GTD = "GTD"
    TimeinForce_DAY = "DAY"
    TimeinForce_AT_THE_OPEN = "AT_THE_OPEN"
    TimeinForce_AT_THE_CLOSE = "AT_THE_CLOSE"

    # Exchange Segments
    EXCHANGE_NSECM = "NSECM"
    EXCHANGE_NSEFO = "NSEFO"
    EXCHANGE_NSECD = "NSECD"
    EXCHANGE_MCXFO = "MCXFO"
    EXCHANGE_BSECM = "BSECM"
    EXCHANGE_BSEFO = "BSEFO"

    # URIs to various calls
    _routes = {
        # Interactive API endpoints
        "interactive.prefix": "interactive",
        "user.login": "/interactive/user/session",
        "user.logout": "/interactive/user/session",
        "user.profile": "/interactive/user/profile",
        "user.balance": "/interactive/user/balance",

        "orders": "/interactive/orders",
        "trades": "/interactive/orders/trades",
        "order.status": "/interactive/orders",
        "order.place": "/interactive/orders",
        "bracketorder.place": "/interactive/orders/bracket",
	    "bracketorder.modify": "/interactive/orders/bracket",
        "bracketorder.cancel": "/interactive/orders/bracket",
        "order.place.cover": "/interactive/orders/cover",
        "order.exit.cover": "/interactive/orders/cover",
        "order.modify": "/interactive/orders",
        "order.cancel": "/interactive/orders",
        "order.cancelall": "/interactive/orders/cancelall",
        "order.history": "/interactive/orders",

        "portfolio.positions": "/interactive/portfolio/positions",
        "portfolio.holdings": "/interactive/portfolio/holdings",
        "portfolio.positions.convert": "/interactive/portfolio/positions/convert",
        "portfolio.squareoff": "/interactive/portfolio/squareoff",
	    "portfolio.dealerpositions": "interactive/portfolio/dealerpositions",
	    "order.dealer.status": "/interactive/orders/dealerorderbook",
	    "dealer.trades": "/interactive/orders/dealertradebook",

        # Market API endpoints
        "marketdata.prefix": "apimarketdata",
        "market.login": "/apimarketdata/auth/login",
        "market.logout": "/apimarketdata/auth/logout",

        "market.config": "/apimarketdata/config/clientConfig",

        "market.instruments.master": "/apimarketdata/instruments/master",
        "market.instruments.subscription": "/apimarketdata/instruments/subscription",
        "market.instruments.unsubscription": "/apimarketdata/instruments/subscription",
        "market.instruments.ohlc": "/apimarketdata/instruments/ohlc",
        "market.instruments.indexlist": "/apimarketdata/instruments/indexlist",
        "market.instruments.quotes": "/apimarketdata/instruments/quotes",

        "market.search.instrumentsbyid": '/apimarketdata/search/instrumentsbyid',
        "market.search.instrumentsbystring": '/apimarketdata/search/instruments',

        "market.instruments.instrument.series": "/apimarketdata/instruments/instrument/series",
        "market.instruments.instrument.equitysymbol": "/apimarketdata/instruments/instrument/symbol",
        "market.instruments.instrument.futuresymbol": "/apimarketdata/instruments/instrument/futureSymbol",
        "market.instruments.instrument.optionsymbol": "/apimarketdata/instruments/instrument/optionsymbol",
        "market.instruments.instrument.optiontype": "/apimarketdata/instruments/instrument/optionType",
        "market.instruments.instrument.expirydate": "/apimarketdata/instruments/instrument/expiryDate"
    }

    def __init__(self,
                 apiKey,
                 secretKey,
                 source,
                 root,
                 debug=False,
                 timeout=1200, # chnaged from 7 to 1200, around 20 minutes.
                 pool=None,
                 disable_ssl=True):
        """
        Initialise a new XTS Connect client instance.

        - `apikey` is the key issued to you
        - `root` is the API end point root. Unless you explicitly
        want to send API requests to a non-default endpoint, this
        can be ignored.
        - `debug`, if set to True, will serialise and print requests
        and responses to stdout.
        - `timeout` is the time (seconds) for which the API client will wait for
        a request to complete before it fails. Defaults to 7 seconds
        - `pool` is manages request pools. It takes a dict of params accepted by HTTPAdapter
        - `disable_ssl` disables the SSL verification while making a request.
        If set requests won't throw SSLError if its set to custom `root` url without SSL.
        """
        self.debug = debug
        self.apiKey = apiKey
        self.secretKey = secretKey
        self.source = source
        self.disable_ssl = disable_ssl
        self.root = root 
        self.timeout = timeout
        self.last_login_time = None   

        super().__init__()

        # Create requests session only if pool exists. Reuse session
        # for every request. Otherwise create session for each request
        if pool:
            self.reqsession = requests.Session()
            reqadapter = requests.adapters.HTTPAdapter(**pool)
            self.reqsession.mount("https://", reqadapter)
        else:
            self.reqsession = requests()

        # disable requests SSL warning
        #requests.packages.urllib3.disable_warnings()

    def _set_common_variables(self, access_token,userID, isInvestorClient):
        """
        Set the `access_token` received after a successful authentication.
        HELPER FUNCTION, DO NOT CALL DIRECTLY.
        """
        super().__init__(access_token,userID, isInvestorClient)

    def _login_url(self):
        """Get the remote login url to which a user should be redirected to initiate the login flow."""
        return self.root +  "/user/session"
    
    def _handle_response(self, response: Dict[str, Any], operation: str) -> Dict[str, Any]:
        """
        Centralized response handler for all API calls.
        
        Args:
            response: The API response dictionary
            operation: Description of the operation (e.g., "Get Order Book", "Place Order")
        
        Returns:
            The response if successful
            
        Raises:
            Exception: If the response indicates an error
        """
        if response.get('type') == 'success':
            return response
        elif response.get('type') == 'error':
            error_msg = f"{operation} failed: {response.get('description', 'Unknown error')}"
            log.error(error_msg)
            raise Exception(error_msg)
        elif response.get('result') is not None:
            # Handle inconsistent XTS API behavior
            return response
        else:
            error_msg = f"{operation} failed: Unexpected response format"
            log.error(error_msg)
            raise Exception(error_msg)

    def _add_client_id(self, params: Dict[str, Any], clientID="*****") -> Dict[str, Any]:
        """Add clientID to params based on isInvestorClient flag."""
        if not self.isInvestorClient:
            params['clientID'] = "*****"
        else:
            params['clientID'] = clientID if clientID else self.userID
        return params

    async def interactive_login(self):
        """
        Send the login url to which a user should receive the token.
        ```
        import os
        API_key = os.getenv("API_KEY")
        API_secret = os.getenv("API_SECRET")
        API_source = os.getenv("API_SOURCE")
        API_root = os.getenv("API_URL")
        """"""""""""""""""""""""""""""""""""""""""
            |DataFrame for Cash Market|
        """"""""""""""""""""""""""""""""""""""""""
        from xts_api_client.xts_connect_async import XTSConnect
        import asyncio

        async def main():
            xt_market_data = XTSConnect(
            apiKey = API_key,
            secretKey = API_secret,
            source = API_source,
            root = API_root
            )
            response_login = await xt_market_data.interactive_login()
            print(f"Loggin In: {response_login}")
            
            await xt_market_data.interactive_logout()
        if __name__ == "__main__":
            asyncio.run(main())
        ```
        """
        params = {
            "appKey": self.apiKey,
            "secretKey": self.secretKey,
            "source": self.source
        }
        response = await self._post("user.login", params)
        if response.get('type') == 'success':
            self._set_common_variables(response['result']['token'], 
                                       response['result']['userID'],
                                       response['result']['isInvestorClient'])
            self._last_login_time = datetime.now(pytz.timezone("Asia/Kolkata"))
            return response
        elif response.get('type') == 'error':
            error_msg = f"Login failed: {response.get('description', 'Unknown error')}"
            log.error(error_msg)
            raise Exception(error_msg)
        else:
            error_msg = "Login failed: Unexpected response format"
            log.error(error_msg)
            raise Exception(error_msg)

    async def get_order_book(self, clientID="*****"):
        """
        Request Order book gives states of all the orders placed by an user.
        IMPORTANT: THIS WILL ONLY WORK AFTER LOGGING IN USING `interactive_login` METHOD.
        ```
        import asyncio
        from xts_api_client.xts_connect_async import XTSConnect
        import os

        API_key = os.getenv("INTERACTIVE_API_KEY")
        API_secret = os.getenv("INTERACTIVE_API_SECRET")
        API_source = os.getenv("API_SOURCE")
        API_root = os.getenv("API_URL")


        xt_interactive = XTSConnect(
        apiKey = API_key,
        secretKey = API_secret,
        source = API_source,
        root = API_root
        )


        async def main():
            await xt_interactive.interactive_login()
            
            respnse_get_order_book_list = await xt_interactive.get_order_book()
            print(f"Order Book: {respnse_get_order_book_list['result']}")
            
            await xt_interactive.interactive_logout()
        if __name__ == "__main__":
            asyncio.run(main())

        ```
        """
        params = {}
        self._add_client_id(params, clientID)
        response = await self._get("order.status", params)
        return self._handle_response(response, "Get Order Book")
		
    async def get_dealer_orderbook(self, clientID="*****"):
        """
        Request Order book gives states of all the orders placed by an user.
        IMPORTANT: THIS WILL ONLY WORK AFTER LOGGING IN USING `interactive_login` METHOD.
        ```
        import asyncio
        from xts_api_client.xts_connect_async import XTSConnect
        import os

        API_key = os.getenv("INTERACTIVE_API_KEY")
        API_secret = os.getenv("INTERACTIVE_API_SECRET")
        API_source = os.getenv("API_SOURCE")
        API_root = os.getenv("API_URL")


        xt_interactive = XTSConnect(
        apiKey = API_key,
        secretKey = API_secret,
        source = API_source,
        root = API_root
        )

        async def main():
            await xt_interactive.interactive_login()
            
            respnse_get_dealer_orderbook = await xt_interactive.get_dealer_orderbook()
            list_of_order_in_dealer_orderbook = respnse_get_dealer_orderbook['result']
            print(f"Dealer Order Book: {list_of_order_in_dealer_orderbook}")
            
            await xt_interactive.interactive_logout()
        if __name__ == "__main__":
            asyncio.run(main())

        ```
        """
        params = {}
        self._add_client_id(params, clientID)
        response = await self._get("order.dealer.status", params)
        return self._handle_response(response, "Get Dealer Order Book")

    async def place_order(self,
                    exchangeSegment,
                    exchangeInstrumentID,
                    productType,
                    orderType,
                    orderSide,
                    timeInForce,
                    disclosedQuantity,
                    orderQuantity,
                    limitPrice,
                    stopPrice,
                    orderUniqueIdentifier,
                    clientID="*****"
                    ):
        """To place an order"""
        params = {
            "exchangeSegment": exchangeSegment,
            "exchangeInstrumentID": exchangeInstrumentID,
            "productType": productType,
            "orderType": orderType,
            "orderSide": orderSide,
            "timeInForce": timeInForce,
            "disclosedQuantity": disclosedQuantity,
            "orderQuantity": orderQuantity,
            "limitPrice": limitPrice,
            "stopPrice": stopPrice,
            "orderUniqueIdentifier": orderUniqueIdentifier
        }
        self._add_client_id(params, clientID)
        response = await self._post('order.place', json.dumps(params))
        return self._handle_response(response, "Place Order")
        
    async def place_bracketorder(self,
                    exchangeSegment,
                    exchangeInstrumentID,
                    orderType,
                    orderSide,
                    disclosedQuantity,
                    orderQuantity,
                    limitPrice,
                    squarOff,
                    stopLossPrice,
	                trailingStoploss,
                    isProOrder,
                    orderUniqueIdentifier,
                    clientID="*****"
                     ):
        """To place a bracketorder"""
        params = {
            "exchangeSegment": exchangeSegment,
            "exchangeInstrumentID": exchangeInstrumentID,
            "orderType": orderType,
            "orderSide": orderSide,
            "disclosedQuantity": disclosedQuantity,
            "orderQuantity": orderQuantity,
            "limitPrice": limitPrice,
            "squarOff": squarOff,
            "stopLossPrice": stopLossPrice,
            "trailingStoploss": trailingStoploss,
            "isProOrder": isProOrder,
            "orderUniqueIdentifier": orderUniqueIdentifier
        }
        self._add_client_id(params, clientID)
        response = await self._post('bracketorder.place', json.dumps(params))
        return self._handle_response(response, "Place Bracket Order")

    async def get_profile(self, clientID="*****"):
        """
        Get a user profile details.
        """
        params = {}
        self._add_client_id(params, clientID)
        response = await self._get('user.profile', params)
        return self._handle_response(response, "Get Profile")

    async def get_balance(self, clientID="*****"):
        """
        Using session token user can access his balance stored with the broker.
        IMPORTANT: THIS WILL ONLY WORK AFTER LOGGING IN USING `interactive_login` METHOD.
        ```
        import asyncio
        from xts_api_client.xts_connect_async import XTSConnect
        import os

        API_key = os.getenv("INTERACTIVE_API_KEY")
        API_secret = os.getenv("INTERACTIVE_API_SECRET")
        API_source = os.getenv("API_SOURCE")
        API_root = os.getenv("API_URL")


        xt_interactive = XTSConnect(
        apiKey = API_key,
        secretKey = API_secret,
        source = API_source,
        root = API_root
        )

        async def main():
            await xt_interactive.interactive_login()
            
            respnse_get_profile = await xt_interactive.get_balance()
            print(f"Cash Available: {respnse_get_profile['result']['BalanceList'][0]['limitObject']['RMSSubLimits']['cashAvailable']}")
            print(f"Net Margin Available: {respnse_get_profile['result']['BalanceList'][0]['limitObject']['RMSSubLimits']['netMarginAvailable']}")
            
            await xt_interactive.interactive_logout()
        if __name__ == "__main__":
            asyncio.run(main())
        ```
        """
        params = {}
        self._add_client_id(params, clientID)
        response = await self._get('user.balance', params)
        return self._handle_response(response, "Get Balance")
   
    async def modify_order(self,
                     appOrderID,
                     modifiedProductType,
                     modifiedOrderType,
                     modifiedOrderQuantity,
                     modifiedDisclosedQuantity,
                     modifiedLimitPrice,
                     modifiedStopPrice,
                     modifiedTimeInForce,
                     orderUniqueIdentifier,
                     clientID="*****"
                     ):
        """The facility to modify your open orders by allowing you to change limit order to market or vice versa,
        change Price or Quantity of the limit open order, change disclosed quantity or stop-loss of any
        open stop loss order. """
        appOrderID = int(appOrderID)
        params = {
            'appOrderID': appOrderID,
            'modifiedProductType': modifiedProductType,
            'modifiedOrderType': modifiedOrderType,
            'modifiedOrderQuantity': modifiedOrderQuantity,
            'modifiedDisclosedQuantity': modifiedDisclosedQuantity,
            'modifiedLimitPrice': modifiedLimitPrice,
            'modifiedStopPrice': modifiedStopPrice,
            'modifiedTimeInForce': modifiedTimeInForce,
            'orderUniqueIdentifier': orderUniqueIdentifier
        }
        self._add_client_id(params, clientID)
        response = await self._put('order.modify', json.dumps(params))
        return self._handle_response(response, "Modify Order")
        
    async def get_trade(self, clientID="*****"):
        """
        Trade book returns a list of all trades executed on a particular day , that were placed by the user . The
        trade book will display all filled and partially filled orders.
        IMPORTANT: THIS WILL ONLY WORK AFTER LOGGING IN USING `interactive_login` METHOD.
        ```
        import asyncio
        from xts_api_client.xts_connect_async import XTSConnect
        import os

        API_key = os.getenv("INTERACTIVE_API_KEY")
        API_secret = os.getenv("INTERACTIVE_API_SECRET")
        API_source = os.getenv("API_SOURCE")
        API_root = os.getenv("API_URL")


        xt_interactive = XTSConnect(
        apiKey = API_key,
        secretKey = API_secret,
        source = API_source,
        root = API_root
        )

        async def main():
            await xt_interactive.interactive_login()
            
            respnse_get_trade = await xt_interactive.get_trade()
            list_of_trade = respnse_get_trade['result']
            print(f"Trade List: {list_of_trade}")
            
            await xt_interactive.interactive_logout()
        if __name__ == "__main__":
            asyncio.run(main())

        ```
        """
        params = {}
        self._add_client_id(params, clientID)
        response = await self._get('trades', params)
        return self._handle_response(response, "Get Trade")
        
    async def get_dealer_tradebook(self, clientID="*****"):
        """
        Trade book returns a list of all trades executed on a particular day , that were placed by the user . The
        trade book will display all filled and partially filled orders.
        IMPORTANT: THIS WILL ONLY WORK AFTER LOGGING IN USING `interactive_login` METHOD.
        ```
        import asyncio
        from xts_api_client.xts_connect_async import XTSConnect
        import os

        API_key = os.getenv("INTERACTIVE_API_KEY")
        API_secret = os.getenv("INTERACTIVE_API_SECRET")
        API_source = os.getenv("API_SOURCE")
        API_root = os.getenv("API_URL")


        xt_interactive = XTSConnect(
        apiKey = API_key,
        secretKey = API_secret,
        source = API_source,
        root = API_root
        )

        async def main():
            await xt_interactive.interactive_login()
            
            respnse_get_dealer_tradebook = await xt_interactive.get_dealer_tradebook()
            list_of_trade = respnse_get_dealer_tradebook['result']
            print(f"Trade List: {list_of_trade}")
            
            await xt_interactive.interactive_logout()
        if __name__ == "__main__":
            asyncio.run(main())

        ```
        """
        params = {}
        self._add_client_id(params, clientID)
        response = await self._get('dealer.trades', params)
        return self._handle_response(response, "Get Dealer Tradebook")
		
    async def get_holding(self, clientID="*****"):
        """
        Holdings API call enable users to check their long term holdings with the broker.
        IMPORTANT: THIS WILL ONLY WORK AFTER LOGGING IN USING `interactive_login` METHOD.
        ```
        import asyncio
        from xts_api_client.xts_connect_async import XTSConnect
        import os


        API_key = os.getenv("INTERACTIVE_API_KEY")
        API_secret = os.getenv("INTERACTIVE_API_SECRET")
        API_source = os.getenv("API_SOURCE")
        API_root = os.getenv("API_URL")


        xt_interactive = XTSConnect(
        apiKey = API_key,
        secretKey = API_secret,
        source = API_source,
        root = API_root
        )

        async def main():
            await xt_interactive.interactive_login()
            
            respnse_get_holding = await xt_interactive.get_holding()
            holding = respnse_get_holding['result']
            print(f"Trade List: {holding}")
            
            await xt_interactive.interactive_logout()
        if __name__ == "__main__":
            asyncio.run(main())

        ```
        """
        params = {}
        self._add_client_id(params, clientID)
        response = await self._get('portfolio.holdings', params)
        return self._handle_response(response, "Get Holding")

    async def bracketorder_cancel(self, appOrderID, clientID="*****"):
        """This API can be called to cancel any open order of the user by providing correct appOrderID matching with
        the chosen open order to cancel. """
        params = {'boEntryOrderId': int(appOrderID)}
        self._add_client_id(params, clientID)
        response = await self._delete('bracketorder.cancel', params)
        return self._handle_response(response, "Cancel Bracket Order")
		
    async def get_dealerposition_netwise(self, clientID="*****"):
        """The positions API positions by net. Net is the actual, current net position portfolio."""
        params = {'dayOrNet': 'NetWise'}
        self._add_client_id(params, clientID)
        response = await self._get('portfolio.dealerpositions', params)
        return self._handle_response(response, "Get Dealer Position Netwise")
           
    async def get_dealerposition_daywise(self, clientID="*****"):
        """The positions API returns positions by day, which is a snapshot of the buying and selling activity for
        that particular day."""
        params = {'dayOrNet': 'DayWise'}
        self._add_client_id(params, clientID)
        response = await self._get('portfolio.dealerpositions', params)
        return self._handle_response(response, "Get Dealer Position Daywise")
		
    async def get_position_daywise(self, clientID="*****"):
	    
        """The positions API returns positions by day, which is a snapshot of the buying and selling activity for
        that particular day."""
        params = {'dayOrNet': 'DayWise'}
        self._add_client_id(params, clientID)
        response = await self._get('portfolio.positions', params)
        return self._handle_response(response, "Get Position Daywise")

    async def get_position_netwise(self, clientID="*****"):
        """The positions API positions by net. Net is the actual, current net position portfolio."""
        params = {'dayOrNet': 'NetWise'}
        self._add_client_id(params, clientID)
        response = await self._get('portfolio.positions', params)
        return self._handle_response(response, "Get Position Netwise")

    async def convert_position(self, exchangeSegment, exchangeInstrumentID, targetQty, isDayWise, oldProductType,
                         newProductType, clientID="*****"):
        """Convert position API, enable users to convert their open positions from NRML intra-day to Short term MIS or
        vice versa, provided that there is sufficient margin or funds in the account to effect such conversion """
        params = {
            'exchangeSegment': exchangeSegment,
            'exchangeInstrumentID': exchangeInstrumentID,
            'targetQty': targetQty,
            'isDayWise': isDayWise,
            'oldProductType': oldProductType,
            'newProductType': newProductType
        }
        self._add_client_id(params, clientID)
        response = await self._put('portfolio.positions.convert', json.dumps(params))
        return self._handle_response(response, "Convert Position")

    async def cancel_order(self, appOrderID, orderUniqueIdentifier, clientID="*****"):
        """This API can be called to cancel any open order of the user by providing correct appOrderID matching with
        the chosen open order to cancel. """
        params = {'appOrderID': int(appOrderID), 'orderUniqueIdentifier': orderUniqueIdentifier}
        self._add_client_id(params, clientID)
        response = await self._delete('order.cancel', params)
        return self._handle_response(response, "Cancel Order")
        
    async def cancelall_order(self, exchangeSegment, exchangeInstrumentID,clientID="*****"):
        """This API can be called to cancel all open order of the user by providing exchange segment and exchange instrument ID """
        params = {"exchangeSegment": exchangeSegment, "exchangeInstrumentID": exchangeInstrumentID}
        self._add_client_id(params, clientID)
        response = await self._post('order.cancelall', json.dumps(params))
        return self._handle_response(response, "Cancel All Order")

    async def place_cover_order(self, exchangeSegment, exchangeInstrumentID, orderSide,orderType, orderQuantity, disclosedQuantity,
                          limitPrice, stopPrice, orderUniqueIdentifier, clientID="*****"):
        """A Cover Order is an advance intraday order that is accompanied by a compulsory Stop Loss Order. This helps
        users to minimize their losses by safeguarding themselves from unexpected market movements. A Cover Order
        offers high leverage and is available in Equity Cash, Equity F&O, Commodity F&O and Currency F&O segments. It
        has 2 orders embedded in itself, they are Limit/Market Order Stop Loss Order """
        params = {'exchangeSegment': exchangeSegment, 'exchangeInstrumentID': exchangeInstrumentID,
                    'orderSide': orderSide, "orderType": orderType,'orderQuantity': orderQuantity, 'disclosedQuantity': disclosedQuantity,
                    'limitPrice': limitPrice, 'stopPrice': stopPrice, 'orderUniqueIdentifier': orderUniqueIdentifier}
        self._add_client_id(params, clientID)
        response = await self._post('order.place.cover', json.dumps(params))
        return self._handle_response(response, "Place Cover Order")

    async def exit_cover_order(self, appOrderID, clientID="*****"):
        """Exit Cover API is a functionality to enable user to easily exit an open stoploss order by converting it
        into Exit order. """
        params = {'appOrderID': appOrderID}
        self._add_client_id(params, clientID)
        response = await self._put('order.exit.cover', json.dumps(params))
        return self._handle_response(response, "Exit Cover Order")

    async def squareoff_position(self, exchangeSegment, exchangeInstrumentID, productType, squareoffMode,
                           positionSquareOffQuantityType, squareOffQtyValue, blockOrderSending, cancelOrders,
                           clientID="*****"):
        """User can request square off to close all his positions in Equities, Futures and Option. Users are advised
        to use this request with caution if one has short term holdings. """
        params = {'exchangeSegment': exchangeSegment, 'exchangeInstrumentID': exchangeInstrumentID,
                    'productType': productType, 'squareoffMode': squareoffMode,
                    'positionSquareOffQuantityType': positionSquareOffQuantityType,
                    'squareOffQtyValue': squareOffQtyValue, 'blockOrderSending': blockOrderSending,
                    'cancelOrders': cancelOrders
                    }
        self._add_client_id(params, clientID)
        response = await self._put('portfolio.squareoff', json.dumps(params))
        return self._handle_response(response, "Squareoff Position")

    async def get_order_history(self, appOrderID, clientID="*****"):
        """Order history will provide particular order trail chain. This indicate the particular order & its state
        changes. i.e.Pending New to New, New to PartiallyFilled, PartiallyFilled, PartiallyFilled & PartiallyFilled
        to Filled etc """
        params = {'appOrderID': appOrderID}
        self._add_client_id(params, clientID)
        response = await self._get('order.history', params)
        return self._handle_response(response, "Get Order History")

    async def interactive_logout(self, clientID="*****"):
        """This call invalidates the session token and destroys the API session. After this, the user should go
        through login flow again and extract session token from login response before further activities. """
        params = {}
        self._add_client_id(params, clientID)
        response = await self._delete('user.logout', params)
        self.token = None # Added this to reset token, so that we can login again.
        return self._handle_response(response, "Interactive Logout")

    async def cancel_order_v2(
            self,
            appOrderID,
            orderUniqueIdentifier,
            TIMEOUT_MAX_RETRIES: int = 4,
            TIMEOUT_RETRY_DELAY: float = 0.5,
            CONNECTIONERROR_MAX_RETRIES: int = 2,
            CONNECTIONERROR_RETRY_DELAY: float = 3,
            clientID="*****"
        ):
            """
            Consistent with place_order_v2:
            - First outer attempt (no loop)
            - Timeout → internal retry loop
            - ConnectError → internal retry loop
            - Other errors → general handler with full debug info
            """

            params = {
                "appOrderID": int(appOrderID),
                "orderUniqueIdentifier": orderUniqueIdentifier,
            }
            params["clientID"] = "*****" if not self.isInvestorClient else self.userID

            response = None  # store any partial server response

            # ===================================================
            # 🔵 OUTER FIRST ATTEMPT (NO LOOP)
            # ===================================================
            try:
                response = await self._delete("order.cancel", params)
                return response   # SUCCESS

            # ===================================================
            # 🟠 TIMEOUT → INTERNAL RETRY LOOP
            # ===================================================
            except (PoolTimeout, ConnectTimeout, ReadTimeout) as e:

                for attempt in range(TIMEOUT_MAX_RETRIES):
                    innerResponse = None
                    try:
                        await asyncio.sleep(TIMEOUT_RETRY_DELAY)
                        innerResponse = await self._delete("order.cancel", params)
                        return innerResponse  # success

                    except (PoolTimeout, ConnectTimeout, ReadTimeout) as inner:
                        if attempt == TIMEOUT_MAX_RETRIES - 1:
                            raise ex.XTSNetworkException(
                                f"[Timeout] Cancel order failed after {TIMEOUT_MAX_RETRIES} retries | "
                                f"appOrderID={appOrderID} | "
                                f"Error={str(inner)} | Response={innerResponse}"
                            ) from inner

                    except Exception as inner:
                        raise ex.XTSInputException(
                            f"[Unknown Error] cancelling order | "
                            f"appOrderID={appOrderID} | "
                            f"Error={str(inner)} | Response={innerResponse}"
                        ) from inner

                # unreachable

            # ===================================================
            # 🔴 CONNECT ERROR → INTERNAL RETRY LOOP
            # ===================================================
            except ConnectError as e:
                innerResponse = None
                print(f"ConnectError caught, entering retry loop...{orderUniqueIdentifier}")

                for attempt in range(CONNECTIONERROR_MAX_RETRIES):
                    try:
                        await asyncio.sleep(CONNECTIONERROR_RETRY_DELAY)
                        innerResponse = await self._delete("order.cancel", params)
                        return innerResponse

                    except ConnectError as inner:
                        print(f"ConnectError retry {attempt + 1} failed.{orderUniqueIdentifier}")
                        if attempt == CONNECTIONERROR_MAX_RETRIES - 1:
                            raise ex.XTSNetworkException(
                                f"[ConnectError] Cancel order failed after {CONNECTIONERROR_MAX_RETRIES} retries | "
                                f"appOrderID={appOrderID} | "
                                f"Error={str(inner)} | Response={innerResponse}"
                            ) from inner

                    except Exception as inner:
                        print(f"Non-ConnectError exception caught in ConnectError retry loop.{orderUniqueIdentifier}")
                        raise ex.XTSInputException(
                            f"[Unknown Error] cancelling order | "
                            f"appOrderID={appOrderID} | "
                            f"Error={str(inner)} | Response={innerResponse}"
                        ) from inner

                # unreachable

            # ===================================================
            # 🔥 ALL OTHER ERRORS (UNCHANGED)
            # ===================================================
            except Exception as e:
                print(f"General exception caught in outer attempt.{orderUniqueIdentifier}")
                raise ex.XTSInputException(
                    f"[Unknown Error] cancelling order | "
                    f"appOrderID={appOrderID} | "
                    f"Error={str(e)} | Response={response}"
                ) from e

    async def modify_order_v2(
        self,
        appOrderID,
        modifiedProductType,
        modifiedOrderType,
        modifiedOrderQuantity,
        modifiedDisclosedQuantity,
        modifiedLimitPrice,
        modifiedStopPrice,
        modifiedTimeInForce,
        orderUniqueIdentifier,
        TIMEOUT_MAX_RETRIES: int = 4,
        TIMEOUT_RETRY_DELAY: float = 0.5,
        CONNECTIONERROR_MAX_RETRIES: int = 2,
        CONNECTIONERROR_RETRY_DELAY: float = 3,
        clientID="*****",
    ):
        """
        Consistent with place_order_v2 + cancel_order_v2:
        - Outer single attempt
        - Timeout → internal retry loop
        - ConnectError → internal retry loop
        - Full response context in all errors
        """

        appOrderID = int(appOrderID)

        params = {
            "appOrderID": appOrderID,
            "modifiedProductType": modifiedProductType,
            "modifiedOrderType": modifiedOrderType,
            "modifiedOrderQuantity": modifiedOrderQuantity,
            "modifiedDisclosedQuantity": modifiedDisclosedQuantity,
            "modifiedLimitPrice": modifiedLimitPrice,
            "modifiedStopPrice": modifiedStopPrice,
            "modifiedTimeInForce": modifiedTimeInForce,
            "orderUniqueIdentifier": orderUniqueIdentifier,
        }

        params["clientID"] = "*****" if not self.isInvestorClient else self.userID

        response = None  # keep partial server response

        # ===================================================
        # 🔵 OUTER FIRST ATTEMPT (NO LOOP)
        # ===================================================
        try:
            response = await self._put("order.modify", json.dumps(params))
            return response  # SUCCESS

        # ===================================================
        # 🟠 TIMEOUT → INTERNAL RETRY LOOP
        # ===================================================
        except (PoolTimeout, ConnectTimeout, ReadTimeout) as e:

            for attempt in range(TIMEOUT_MAX_RETRIES):
                innerResponse = None
                try:
                    await asyncio.sleep(TIMEOUT_RETRY_DELAY)
                    innerResponse = await self._put("order.modify", json.dumps(params))
                    return innerResponse  # SUCCESS

                except (PoolTimeout, ConnectTimeout, ReadTimeout) as inner:
                    if attempt == TIMEOUT_MAX_RETRIES - 1:
                        raise ex.XTSNetworkException(
                            f"[Timeout] Modify order failed after {TIMEOUT_MAX_RETRIES} retries | "
                            f"AppOrderID={appOrderID} | "
                            f"Error={str(inner)} | Response={innerResponse}"
                        ) from inner

                except Exception as inner:
                    raise ex.XTSInputException(
                        f"[Unknown Error] modifying order | "
                        f"AppOrderID={appOrderID} | "
                        f"Error={str(inner)} | Response={innerResponse}"
                    ) from inner

            # unreachable

        # ===================================================
        # 🔴 CONNECT ERROR → INTERNAL RETRY LOOP
        # ===================================================
        except ConnectError as e:
            innerResponse = None
            print(f"ConnectError caught, entering retry loop...{orderUniqueIdentifier}")

            for attempt in range(CONNECTIONERROR_MAX_RETRIES):
                try:
                    await asyncio.sleep(CONNECTIONERROR_RETRY_DELAY)
                    innerResponse = await self._put("order.modify", json.dumps(params))
                    return innerResponse

                except ConnectError as inner:
                    print(f"ConnectError retry {attempt + 1} failed.{orderUniqueIdentifier}")
                    if attempt == CONNECTIONERROR_MAX_RETRIES - 1:
                        raise ex.XTSNetworkException(
                            f"[ConnectError] Modify order failed after {CONNECTIONERROR_MAX_RETRIES} retries | "
                            f"AppOrderID={appOrderID} | "
                            f"Error={str(inner)} | Response={innerResponse}"
                        ) from inner

                except Exception as inner:
                    print(f"Non-ConnectError exception caught in ConnectError retry loop.{orderUniqueIdentifier}")
                    raise ex.XTSInputException(
                        f"[Unknown Error] modifying order | "
                        f"AppOrderID={appOrderID} | "
                        f"Error={str(inner)} | Response={innerResponse}"
                    ) from inner

            # unreachable

        # ===================================================
        # 🔥 ALL OTHER ERRORS
        # ===================================================
        except Exception as e:
            print(f"General exception caught in outer attempt.{orderUniqueIdentifier}")
            raise ex.XTSInputException(
                f"[Unknown Error] modifying order | "
                f"AppOrderID={appOrderID} | "
                f"Error={str(e)} | Response={response}"
            ) from e

    async def place_order_v2(
    self,
    exchangeSegment,
    exchangeInstrumentID,
    productType,
    orderType,
    orderSide,
    timeInForce,
    disclosedQuantity,
    orderQuantity,
    limitPrice,
    stopPrice,
    orderUniqueIdentifier,
    TIMEOUT_MAX_RETRIES: int = 4,
    TIMEOUT_RETRY_DELAY: float = 0.5,
    CONNECTIONERROR_MAX_RETRIES: int = 2,
    CONNECTIONERROR_RETRY_DELAY: float = 3,
    clientID="*****"
    ):
            
        """
        FIRST outer attempt (single try).
        If timeout → do a FOR LOOP retry INSIDE the timeout handler.
        If ConnectError → do a FOR LOOP retry INSIDE the connect error handler.
        """

        params = {
            "exchangeSegment": exchangeSegment,
            "exchangeInstrumentID": exchangeInstrumentID,
            "productType": productType,
            "orderType": orderType,
            "orderSide": orderSide,
            "timeInForce": timeInForce,
            "disclosedQuantity": disclosedQuantity,
            "orderQuantity": orderQuantity,
            "limitPrice": limitPrice,
            "stopPrice": stopPrice,
            "orderUniqueIdentifier": orderUniqueIdentifier,
        }

        params["clientID"] = "*****" if not self.isInvestorClient else self.userID

        response = None  # keep reference

        # ===================================================
        # 🔵 OUTER FIRST ATTEMPT (NO LOOP)
        # ===================================================
        try:
            response = await self._post("order.place", json.dumps(params))
            return response   # SUCCESS

        # ===================================================
        # 🟠 TIMEOUT → INTERNAL RETRY LOOP
        # ===================================================
        except (PoolTimeout, ConnectTimeout, ReadTimeout) as e:

            for attempt in range(TIMEOUT_MAX_RETRIES):
                innerResponse = None
                try:
                    await asyncio.sleep(TIMEOUT_RETRY_DELAY)
                    innerResponse = await self._post("order.place", json.dumps(params))
                    return innerResponse   # success
                except (PoolTimeout, ConnectTimeout, ReadTimeout) as inner:
                    if attempt == TIMEOUT_MAX_RETRIES - 1:
                        raise ex.XTSNetworkException(
                            f"[Timeout] Order failed after {TIMEOUT_MAX_RETRIES} retries | "
                            f"InstrumentID={exchangeInstrumentID} Qty={orderQuantity} | "
                            f"Error={str(inner)} | Response={innerResponse}"
                        ) from inner
                except Exception as inner:

                    raise ex.XTSInputException(
                        f"[Unknown Error] placing order | "
                        f"InstrumentID={exchangeInstrumentID} Qty={orderQuantity} | "
                        f"Error={str(inner)} | Response={innerResponse}"
                    ) from inner

            # unreachable due to return/raise

        # ===================================================
        # 🔴 CONNECT ERROR → INTERNAL RETRY LOOP
        # ===================================================
        except ConnectError as e:
            innerResponse = None
            print(f"ConnectError caught, entering retry loop...{orderUniqueIdentifier}")
            
            for attempt in range(CONNECTIONERROR_MAX_RETRIES):
                try:
                    await asyncio.sleep(CONNECTIONERROR_RETRY_DELAY)
                    innerResponse = await self._post("order.place", json.dumps(params))
                    return innerResponse
                except ConnectError as inner:
                    print(f"ConnectError retry attempt {attempt + 1} failed.{orderUniqueIdentifier}")
                    if attempt == CONNECTIONERROR_MAX_RETRIES - 1:
                        raise ex.XTSNetworkException(
                            f"[ConnectError] Order failed after {CONNECTIONERROR_MAX_RETRIES} retries | "
                            f"InstrumentID={exchangeInstrumentID} Qty={orderQuantity} | "
                            f"Error={str(inner)} | Response={innerResponse}"
                        ) from inner
                except Exception as inner:
                    print(f"Non-ConnectError exception caught in ConnectError retry loop.{orderUniqueIdentifier}")
                    raise ex.XTSInputException(
                        f"[Unknown Error] placing order | "
                        f"InstrumentID={exchangeInstrumentID} Qty={orderQuantity} | "
                        f"Error={str(inner)} | Response={innerResponse}"
                    ) from inner

            # unreachable

        # ===================================================
        # 🔥 ALL OTHER ERRORS (UNCHANGED)
        # ===================================================
        except Exception as e:
            print(f"General exception caught in outer attempt.{orderUniqueIdentifier}")
            raise ex.XTSInputException(
                f"[Unknown Error] placing order | "
                f"InstrumentID={exchangeInstrumentID} Qty={orderQuantity} | "
                f"Error={str(e)} | Response={response}"
            ) from e

    async def cancelall_order_v2(
            self,
            exchangeSegment,
            exchangeInstrumentID,
            TIMEOUT_MAX_RETRIES: int = 4,
            TIMEOUT_RETRY_DELAY: float = 0.5,
            CONNECTIONERROR_MAX_RETRIES: int = 2,
            CONNECTIONERROR_RETRY_DELAY: float = 3,
            clientID="*****"
        ):
            """
            V2 version consistent with cancel_order_v2 & place_order_v2:
            - Outer first attempt (no loop)
            - Timeout → internal retry loop
            - ConnectError → internal retry loop
            - Other errors → general exception with full debug information
            """

            # Prepare params
            params = {
                "exchangeSegment": exchangeSegment,
                "exchangeInstrumentID": exchangeInstrumentID,
            }

            params["clientID"] = "*****" if not self.isInvestorClient else self.userID

            response = None  # store partial response

            # ===================================================
            # 🔵 OUTER FIRST ATTEMPT (NO LOOP)
            # ===================================================
            try:
                response = await self._post("order.cancelall", json.dumps(params))
                return response  # success

            # ===================================================
            # 🟠 TIMEOUT → INTERNAL RETRY LOOP
            # ===================================================
            except (PoolTimeout, ConnectTimeout, ReadTimeout) as e:

                for attempt in range(TIMEOUT_MAX_RETRIES):
                    innerResponse = None
                    try:
                        await asyncio.sleep(TIMEOUT_RETRY_DELAY)
                        innerResponse = await self._post("order.cancelall", json.dumps(params))
                        return innerResponse  # success

                    except (PoolTimeout, ConnectTimeout, ReadTimeout) as inner:
                        if attempt == TIMEOUT_MAX_RETRIES - 1:
                            raise ex.XTSNetworkException(
                                f"[Timeout] CancelAll order failed after {TIMEOUT_MAX_RETRIES} retries | "
                                f"exchangeSegment={exchangeSegment} | "
                                f"exchangeInstrumentID={exchangeInstrumentID} | "
                                f"Error={str(inner)} | Response={innerResponse}"
                            ) from inner

                    except Exception as inner:
                        raise ex.XTSInputException(
                            f"[Unknown Error] cancelling all orders | "
                            f"exchangeSegment={exchangeSegment} | "
                            f"exchangeInstrumentID={exchangeInstrumentID} | "
                            f"Error={str(inner)} | Response={innerResponse}"
                        ) from inner

                # unreachable

            # ===================================================
            # 🔴 CONNECT ERROR → INTERNAL RETRY LOOP
            # ===================================================
            except ConnectError as e:
                innerResponse = None
                print(f"ConnectError caught, entering retry loop...{exchangeSegment}:{exchangeInstrumentID}")

                for attempt in range(CONNECTIONERROR_MAX_RETRIES):
                    try:
                        await asyncio.sleep(CONNECTIONERROR_RETRY_DELAY)
                        innerResponse = await self._post("order.cancelall", json.dumps(params))
                        return innerResponse

                    except ConnectError as inner:
                        print(f"ConnectError retry {attempt + 1} failed.{exchangeSegment}:{exchangeInstrumentID}")
                        if attempt == CONNECTIONERROR_MAX_RETRIES - 1:
                            raise ex.XTSNetworkException(
                                f"[ConnectError] CancelAll order failed after {CONNECTIONERROR_MAX_RETRIES} retries | "
                                f"exchangeSegment={exchangeSegment} | "
                                f"exchangeInstrumentID={exchangeInstrumentID} | "
                                f"Error={str(inner)} | Response={innerResponse}"
                            ) from inner

                    except Exception as inner:
                        print(f"Non-ConnectError exception caught in ConnectError retry loop.{exchangeSegment}:{exchangeInstrumentID}")
                        raise ex.XTSInputException(
                            f"[Unknown Error] cancelling all orders | "
                            f"exchangeSegment={exchangeSegment} | "
                            f"exchangeInstrumentID={exchangeInstrumentID} | "
                            f"Error={str(inner)} | Response={innerResponse}"
                        ) from inner

                # unreachable

            # ===================================================
            # 🔥 ALL OTHER ERRORS
            # ===================================================
            except Exception as e:
                print(f"General exception caught in outer attempt.{exchangeSegment}:{exchangeInstrumentID}")
                raise ex.XTSInputException(
                    f"[Unknown Error] cancelling all orders | "
                    f"exchangeSegment={exchangeSegment} | "
                    f"exchangeInstrumentID={exchangeInstrumentID} | "
                    f"Error={str(e)} | Response={response}"
                ) from e

    ########################################################################################################
    # Market data API
    ########################################################################################################

    async def marketdata_login(self):
        """Send the login url to which a user should receive the token."""
        params = {
            "appKey": self.apiKey,
            "secretKey": self.secretKey,
            "source": self.source
        }
        response = await self._post("market.login", params)

        if response.get('type') == 'success':
            self._set_common_variables(response['result']['token'],
                                       response['result']['userID'],False)
            self._last_login_time = datetime.now(pytz.timezone("Asia/Kolkata"))
            return response
        elif response.get('type') == 'error':
            error_msg = f'API responded with error: {response.get("description","Unknown error")}'
            log.error(error_msg)
            raise Exception(error_msg)
        else:
            error_msg = 'Unexpected API response format.'
            log.error(error_msg)
            raise Exception(error_msg)
        
    async def get_config(self):
        """Get the configuration of the client."""
        params = {}
        response = await self._get('market.config', params)
        return self._handle_response(response, "Get Config")

    async def get_quote(self, Instruments, xtsMessageCode, publishFormat):
        """Get the quote of the instrument."""
        params = {'instruments': Instruments, 'xtsMessageCode': xtsMessageCode, 'publishFormat': publishFormat}
        response = await self._post('market.instruments.quotes', json.dumps(params))
        return self._handle_response(response, "Get Quote")

    async def send_subscription(
        self,
        Instruments: List[Dict[str, int]],
        xtsMessageCode: int
        ):
        """
        Sends a subscription request for the specified instruments using the given XTS message code.

        Parameters:
            Instruments (List[Dict[str, int]]): A list of instruments to subscribe to. 
                Each instrument should be a dictionary with:
                    - 'exchangeSegment' (int): Numeric code for the exchange segment.
                    - 'exchangeInstrumentID' (int): Unique instrument ID in that segment.

                Example:
                    instruments = [
                        {'exchangeSegment': 1, 'exchangeInstrumentID': 2885},
                        {'exchangeSegment': 1, 'exchangeInstrumentID': 22}
                    ]

            xtsMessageCode (int): XTS message code specifying the type of subscription.
                For example:
                -TouchlineEvent               = 1501,
                -MarketDepthEvent             = 1502,
                -IndexDataEvent               = 1504,
                -CandleDataEvent              = 1505,
                -InstrumentPropertyChangeEvent = 1105,
                -OpenInterestEvent            = 1510,
                -LTPEvent                     = 1512
            """
        params = {'instruments': Instruments, 'xtsMessageCode': xtsMessageCode}
        response = await self._post('market.instruments.subscription', json.dumps(params))
        return self._handle_response(response, "Send Subscription")

    async def send_unsubscription(
        self,
        Instruments: List[Dict[str, int]],
        xtsMessageCode: int
        ):
        """
        Sends an unsubscription request to stop receiving data for the specified instruments.

        Parameters:
            Instruments (List[Dict[str, int]]): A list of instruments to unsubscribe from. 
                Each instrument should be a dictionary containing:
                    - 'exchangeSegment' (int): Numeric code for the exchange segment.
                    - 'exchangeInstrumentID' (int): Unique instrument ID in that segment.

                Example:
                    instruments = [
                        {'exchangeSegment': 1, 'exchangeInstrumentID': 2885},
                        {'exchangeSegment': 1, 'exchangeInstrumentID': 22}
                    ]

            xtsMessageCode (int): XTS message code specifying the type of subscription to cancel.
                For example:
                -TouchlineEvent               = 1501,
                -MarketDepthEvent             = 1502,
                -IndexDataEvent               = 1504,
                -CandleDataEvent              = 1505,
                -InstrumentPropertyChangeEvent = 1105,
                -OpenInterestEvent            = 1510,
                -LTPEvent                     = 1512
        """
        params = {'instruments': Instruments, 'xtsMessageCode': xtsMessageCode}
        response = await self._put('market.instruments.unsubscription', json.dumps(params))
        return self._handle_response(response, "Send Unsubscription")

    async def get_master(
        self,
        exchangeSegmentList: List[int]
        ):
        """Get the master string."""
        params = {"exchangeSegmentList": exchangeSegmentList}
        response = await self._post('market.instruments.master', json.dumps(params))
        return self._handle_response(response, "Get Master")
        
    async def get_ohlc(
        self,
        exchangeSegment: int,
        exchangeInstrumentID: int,
        startTime: str,
        endTime: str,
        compressionValue: int):
        """
        Retrieves historical OHLC (Open, High, Low, Close) candle data for a given instrument.

        Parameters:
        exchangeSegment (int): Numeric identifier for the exchange segment. Supported values:
                - 1  = NSECM
                - 2  = NSEFO
                - 3  = NSECD
                - 4  = NSECO
                - 5  = SLBM
                - 7  = NIFSC
                - 11 = BSECM
                - 12 = BSEFO
                - 13 = BSECD
                - 14 = BSECO
                - 21 = NCDEX
                - 41 = MSECM
                - 42 = MSEFO
                - 43 = MSECD
                - 51 = MCXFO

        exchangeInstrumentID (int): Unique instrument ID for the given exchange segment.
            - For NSECM, use NSE instrument ID (e.g., 22 for NIFTY).
            - For BSECM, use BSE instrument ID (e.g., "526530").

        startTime (str): Start time for the OHLC data in the format "MMM DD YYYY HHMMSS", e.g., "Dec 02 2024 091500".
            - Time is in IST (Indian Standard Time).

        endTime (str): End time for the OHLC data in the format "MMM DD YYYY HHMMSS", e.g., "Dec 02 2024 133000".
            - Time is in IST and uses 24-hour format.

        compressionValue (int): Timeframe for each candle in minutes.
            - For example, 1 for 1-minute candles, 60 for hourly candles, etc.
        """
        params = {
            'exchangeSegment': exchangeSegment,
            'exchangeInstrumentID': exchangeInstrumentID,
            'startTime': startTime,
            'endTime': endTime,
            'compressionValue': compressionValue}
        response = await self._get('market.instruments.ohlc', params)
        return self._handle_response(response, "Get OHLC")

    async def get_series(
        self,
        exchangeSegment: int
        ):
        """ 
        Retrieves the series of instruments available on a specific exchange segment.
        Parameters:
        exchangeSegment (int): Numeric identifier for the exchange segment. Supported values include:
                - 1  = NSECM
                - 2  = NSEFO
                - 3  = NSECD
                - 4  = NSECO
                - 5  = SLBM
                - 7  = NIFSC
                - 11 = BSECM
                - 12 = BSEFO
                - 13 = BSECD
                - 14 = BSECO
                - 21 = NCDEX
                - 41 = MSECM
                - 42 = MSEFO
                - 43 = MSECD
                - 51 = MCXFO
        """
        params = {'exchangeSegment': exchangeSegment}
        response = await self._get('market.instruments.instrument.series', params)
        return self._handle_response(response, "Get Series")

    async def get_equity_symbol(
        self,
        exchangeSegment: int,
        series: str,
        symbol: str
        ):
        """ 
        Retrieves the full equity symbol for a given instrument based on the exchange segment, series, and trading symbol.

        Parameters:
        exchangeSegment (int): Numeric identifier for the exchange segment. Supported values include:
                - 1  = NSECM
                - 2  = NSEFO
                - 3  = NSECD
                - 4  = NSECO
                - 5  = SLBM
                - 7  = NIFSC
                - 11 = BSECM
                - 12 = BSEFO
                - 13 = BSECD
                - 14 = BSECO
                - 21 = NCDEX
                - 41 = MSECM
                - 42 = MSEFO
                - 43 = MSECD
                - 51 = MCXFO

        series (str): Series type for the equity, such as:
            - "EQ"  = Equity
            - "BE"  = Trade-to-trade segment
            - "BL", "BZ", etc., as applicable

        symbol (str): Trading symbol of the security, e.g., "RELIANCE", "TATAMOTORS", "INFY".
        
        """
        params = {'exchangeSegment': exchangeSegment, 'series': series, 'symbol': symbol}
        response = await self._get('market.instruments.instrument.equitysymbol', params)
        return self._handle_response(response, "Get Equity Symbol")

    async def get_expiry_date(
        self,
        exchangeSegment: int,
        series: str,
        symbol: str
        ):
        """
        Retrieves the available expiry dates for a given instrument on the specified exchange segment.

        Parameters:
            exchangeSegment (int): Numeric identifier for the exchange segment. Accepted values include:
                - 1  = NSECM
                - 2  = NSEFO
                - 3  = NSECD
                - 4  = NSECO
                - 5  = SLBM
                - 7  = NIFSC
                - 11 = BSECM
                - 12 = BSEFO
                - 13 = BSECD
                - 14 = BSECO
                - 21 = NCDEX
                - 41 = MSECM
                - 42 = MSEFO
                - 43 = MSECD
                - 51 = MCXFO

            series (str): Series type, such as "FUT", "OPTIDX", "OPTSTK", etc.

            symbol (str): Trading symbol of the instrument, e.g., "NIFTY", "BANKNIFTY", "RELIANCE".
        """
        params = {'exchangeSegment': exchangeSegment, 'series': series, 'symbol': symbol}
        response = await self._get('market.instruments.instrument.expirydate', params)
        return self._handle_response(response, "Get Expiry Date")

    async def get_future_symbol(
        self,
        exchangeSegment: int,
        series: str,
        symbol: str,
        expiryDate: str
        ):
        """
        Retrieves the future symbol for the specified instrument parameters.

        Parameters:
            exchangeSegment (int): Numeric identifier for the exchange segment. Accepted values include:
                - 1  = NSECM
                - 2  = NSEFO
                - 3  = NSECD
                - 4  = NSECO
                - 5  = SLBM
                - 7  = NIFSC
                - 11 = BSECM
                - 12 = BSEFO
                - 13 = BSECD
                - 14 = BSECO
                - 21 = NCDEX
                - 41 = MSECM
                - 42 = MSEFO
                - 43 = MSECD
                - 51 = MCXFO

            series (str): Series type, such as "FUTIDX", "FUTSTK", or other valid series codes.

            symbol (str): Trading symbol of the instrument, e.g., "NIFTY", "BANKNIFTY", "RELIANCE".

            expiryDate (str): Expiry date of the futures contract in the format "DDMMMYYYY", e.g., "26Jun2025".

        """
        params = {'exchangeSegment': exchangeSegment, 'series': series, 'symbol': symbol, 'expiryDate': expiryDate}
        response = await self._get('market.instruments.instrument.futuresymbol', params)
        return self._handle_response(response, "Get Future Symbol")

    async def get_option_symbol(
        self,
        exchangeSegment: int,
        series: str,
        symbol: str,
        expiryDate: str,
        optionType: str,
        strikePrice: float
        ):
        """
        Retrieves the option symbol for a given set of parameters from the specified exchange segment.

        Parameters:
            exchangeSegment (int): Numeric identifier for the exchange segment. Accepted values include:
                - 1  = NSECM
                - 2  = NSEFO
                - 3  = NSECD
                - 4  = NSECO
                - 5  = SLBM
                - 7  = NIFSC
                - 11 = BSECM
                - 12 = BSEFO
                - 13 = BSECD
                - 14 = BSECO
                - 21 = NCDEX
                - 41 = MSECM
                - 42 = MSEFO
                - 43 = MSECD
                - 51 = MCXFO

            series (str): Series type, such as "EQ", "FUT", "OPTIDX", "OPTSTK", "OPTFO".

            symbol (str): Trading symbol of the instrument, e.g., "RELIANCE", "TATAMOTORS", "NIFTY", "BANKNIFTY".

            expiryDate (str): Expiry date of the option in the format "DDMMMYYYY", e.g., "26Jun2025".

            optionType (str): Type of option - "CE" for Call Option or "PE" for Put Option.

            strikePrice (float): Strike price of the option, e.g., 24500.0.

        """
        params = {'exchangeSegment': exchangeSegment, 'series': series, 'symbol': symbol, 'expiryDate': expiryDate,
                    'optionType': optionType, 'strikePrice': strikePrice}
        response = await self._get('market.instruments.instrument.optionsymbol', params)
        return self._handle_response(response, "Get Option Symbol")

    async def get_option_type(
        self,
        exchangeSegment: int,
        series: str,
        symbol: str,
        expiryDate: str):
        """
        Retrieves the available option types (Call/Put) for a given instrument and expiry date 
        on the specified exchange segment.

        Parameters:
            exchangeSegment (int): Numeric identifier for the exchange segment. Accepted values include:
                - 1  = NSECM
                - 2  = NSEFO
                - 3  = NSECD
                - 4  = NSECO
                - 5  = SLBM
                - 7  = NIFSC
                - 11 = BSECM
                - 12 = BSEFO
                - 13 = BSECD
                - 14 = BSECO
                - 21 = NCDEX
                - 41 = MSECM
                - 42 = MSEFO
                - 43 = MSECD
                - 51 = MCXFO

            series (str): Series type, such as "OPTIDX", "OPTSTK", or similar.

            symbol (str): Trading symbol of the instrument, e.g., "NIFTY", "BANKNIFTY", "RELIANCE".

            expiryDate (str): Expiry date in the format "DDMMMYYYY", e.g., "26Jun2025".

            """
        params = {'exchangeSegment': exchangeSegment, 'series': series, 'symbol': symbol, 'expiryDate': expiryDate}
        response = await self._get('market.instruments.instrument.optiontype', params)
        return self._handle_response(response, "Get Option Type")

    async def get_index_list(self, exchangeSegment):
        """ Get the index list of the exchange segment."""
        params = {'exchangeSegment': exchangeSegment}
        response = await self._get('market.instruments.indexlist', params)
        return self._handle_response(response, "Get Index List")

    async def search_by_instrumentid(self, Instruments):
        """ Search by instrument id.\n
        eg. \n
        ```
            instruments = [{"exchangeSegment":2,"exchangeInstrumentID":47631}]
            resp = await xt_market_data.search_by_instrumentid(instruments)
        ```
        """
        params = {'source': self.source, 'instruments': Instruments}
        response = await self._post('market.search.instrumentsbyid', json.dumps(params))
        return self._handle_response(response, "Search by Instrument ID")

    async def search_by_scriptname(self, searchString):
        """ Search by script name."""
        params = {'searchString': searchString}
        response = await self._get('market.search.instrumentsbystring', params)
        return self._handle_response(response, "Search by Script Name")

    async def marketdata_logout(self):
        """This call invalidates the session token and destroys the API session. After this, the user should go"""
        params = {}
        response = await self._delete('market.logout', params)
        self.token = None # Added this to reset token, so that we can login again.
        return self._handle_response(response, "Market Data Logout")

    ########################################################################################################
    # Common Methods
    ########################################################################################################

    async def _get(self, route, params=None):
        """Alias for sending a GET request."""
        return await self._request(route, "GET", params)

    async def _post(self, route, params=None):
        """Alias for sending a POST request."""
        return await self._request(route, "POST", params)

    async def _put(self, route, params=None):
        """Alias for sending a PUT request."""
        return await self._request(route, "PUT", params)

    async def _delete(self, route, params=None):
        """Alias for sending a DELETE request."""
        return await self._request(route, "DELETE", params)

    async def _request(self, route, method, parameters=None):
        """Make an HTTP request."""
        params = parameters if parameters else {}

        # Form a restful URL
        uri = self._routes[route].format(params)
        url = urljoin(self.root, uri)
        headers = {}

        if self.token:
            # set authorization header
            headers.update({'Content-Type': 'application/json', 'Authorization': self.token})

        try:
            # r = await self.reqsession.request(method,
            #                             url,
            #                             data=params if method in ["POST", "PUT"] else None,
            #                             params=params if method in ["GET", "DELETE"] else None,
            #                             headers=headers,
            #                             verify=not self.disable_ssl)

            r = await self.reqsession.request(method = method, url = url,
                                        data=params if method in ["POST", "PUT"] else None,
                                        params=params if method in ["GET", "DELETE"] else None,
                                        headers=headers)
        except Exception as e:
            #log the full stack trace for debugging
            log.error(f"Request failed for {method} {url} with error: {str(e)}")
            log.error(f"Stack trace:\n{traceback.format_exc()}")
            raise e

        if self.debug:
            log.debug("Response: {code} {content}".format(code=r.status_code, content=r.content))

        # Validate the content type.
        if "json" in r.headers["content-type"]:
            try:
                data = json.loads(r.content.decode("utf8"))
            except ValueError:
                log.error(f"JSON parsing failed for response content: {r.content}")
                log.error(f"Stack trace:\n{traceback.format_exc()}")
                raise ex.XTSDataException("Couldn't parse the JSON response received from the server: {content}".format(
                    content=r.content))

            # api error
            if data.get("type"):

                if r.status_code == 400 and data["type"] == "error" and data["description"] == "Invalid Token":
                    raise ex.XTSTokenException(data["description"])

                if r.status_code == 400 and data["type"] == "error" and data["description"] == "Bad Request":
                    message = f"Description: {data['description']} errors: {str(data['result'].get('errors', []))}"
                    raise ex.XTSInputException(str(message))

            return data
        else:
            log.error(f"Invalid Content-Type: {r.headers.get('content-type','')} for response content: {r.content}")
            raise ex.XTSDataException("Unknown Content-Type ({content_type}) with response: ({content})".format(
                content_type=r.headers.get("content-type"),
                content=r.content))
