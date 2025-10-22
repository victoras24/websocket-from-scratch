I knew how WebSockets worked from a high-level perspective, but I wanted to dive in at a lower level as low as I could with my tech stack (C# and JavaScript). I used RFC 6455 as a guide to implement WebSockets and also relied on LLMs to help me find the right methods to use in C#. For example, I asked how to initiate a TCP listener.

I discovered that if you add “Please don’t code the response” at the end of your prompt, the LLM gives you a clear step-by-step guide. That way, you actually have to think and build the solution yourself instead of just copy-pasting.

First, I created the variables I was going to use frequently in the code: the port number, the newline text and a byte buffer. The port number is where the client and server will communicate.

Then I created a TCP listener and started listening for incoming requests. Once we receive a request, we enter a loop. We get the stream from the request and decode the bytes using UTF-8. Now we can proceed with handling the request and checking whether we should upgrade the HTTP request to WebSocket.

To determine that, we check the handshake headers from the client for the following:
~~~~~~~~~~~~~
Upgrade: websocket
Connection: Upgrade
~~~~~~~~~~~~~
If those two headers are present, it means the client wants to upgrade the connection to WebSocket. We then take the Sec-WebSocket-Key header, append the magic string from the RFC:
~~~~~~~~~~~~~
258EAFA5-E914-47DA-95CA-C5AB0DC85B11
~~~~~~~~~~~~~

and use it to compute the Sec-WebSocket-Accept value. (Important note: this string must be exactly as specified in the RFC. If it’s changed, the handshake will fail.)
Finally, we send back this response:
~~~~~~~~~~~~~
HTTP/1.1 101 Switching Protocols
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Accept: <computed-value>
EMPTY LINE <--MUST
~~~~~~~~~~~~~

If the request does not contain the required headers, we simply respond with a normal HTTP response and do not upgrade the connection to WebSocket.

The next part is decoding the payload sent by the client. This was the hard part for me because I didn’t know anything about binary data, bitwise operators, or how to read the figure in the RFC (screenshot below): 
  
<img width="1400" height="760" alt="image" src="https://github.com/user-attachments/assets/6231e6f5-9d4d-45eb-9d8c-f3d453f68925" />

If it’s your first time working with binary data, I feel you. So let me explain.
Each byte is 8 bits. The second row with the digits 0, 1, 2, 3 and so on represents the bit positions. For example, FIN (which I’ll explain shortly) uses 1 bit. RSV1, RSV2 and RSV3 each use 1 bit as well. Then the opcode uses 4 bits. Altogether, these make up the first byte of the frame.

Why do we need this kind of diagram? Because once the connection is upgraded to WebSocket, all client messages are sent as frames in binary format. This figure shows us how a frame is structured. To access the data the client sends, we need to decode these binary frames.

Let’s take the FIN bit as an example. According to the RFC, FIN indicates whether this is the last frame of a message or if more frames are coming.

- If it’s 1, this is the final frame of the message.

- If it’s 0, more continuation frames will follow.

So can we just read the first bit and get the value? Not directly  we need to use bitwise operators to extract it.

In C#, the bitwise operators are AND (&), OR (|), XOR (^), shift left (<<) and shift right (>>). I ended up using all of them while decoding the frame.

Let’s start with the AND operator for the FIN bit. The & operator compares each bit of two bytes and returns 1 only if both bits are 1. Otherwise, it returns 0. This produces a new binary value.

For example, let’s say the first byte we received is 10000001 (decimal 129).
To isolate the first bit (the most significant bit, or MSB), we use a mask of 10000000 (decimal 128).
  
Why 10000000?  
  
The first bit in a byte is called MSB (most significant bit) and the last one is called LSB (least significant bit). The least significant bit represents the lowest power of two $2^{0}$ meaning equals to 1. Then the next one 
is $2^{1}$ = 2 and so on. The MSB is $2^{7}$ = 128. Then why we chose 1000000? So that equals to 128 and the first byte let's say is 129 = 10000001. In that way we will have:
~~~~~~~~~~~~~
10000000  
&  
10000001  
=======
1000000  
~~~~~~~~~~~~~
The result tells us that the first bit is 1, which means FIN = 1. That indicates this is the final frame of the message and no more continuation frames are coming for this particular message.  
The next 3 bits are the RSV1, RSV2, and RSV3. Most of the time, they are 0, but rarely they are 1. They are reserved for specific cases. Let’s continue to the last 4 bits of the 1st byte. These are the opcode, which describes the data format of the payload. To isolate the last 4 bits of the byte, we use a mask of 00001111.
~~~~~~~~~~~~~  
00001111  
&  
10000001  
=======
00000001  
~~~~~~~~~~~~~
The result tells us that the last 4 bits are 1. According to the RFC, an opcode of 1 means the payload data is in text form (refer to the RFC for other format codes).
And just like that, we’ve masked the first byte of the frame. I did the same for the payload length, extended payload length, masking-key, and finally the payload data. I won’t go into more detail here, as it’s better explained in the RFC.

What are the next steps?

1. Fragmentation
2. Ping-Pong
3. Error handling

It may have taken me a week to set up a WebSocket from scratch, but now I have a better understanding of how WebSockets are created, how the connection is maintained, what the frames are, and how the frames are decoded. I encourage all developers, especially junior ones like me, to give it a try. Sometimes, when you dig into the low level details, everything feels reasonable and easy to understand. But when you try to look at it from a high level perspective, it can seem really complex.

