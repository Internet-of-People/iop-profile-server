# Tests and Logs

## Use Profile Server Logs

During the development of your client, you might receive (and you probably will receive) unexpected errors from the profile server 
and you might fail to understand what is going on. In such cases, the fastest way is usually to inspect the profile server logs. 
The profile server creates very extensive logs about every action it does and you can find additional information about the cause 
of the returned error. Always check the server logs and then check the documentation to see if you could find the root of the problem.


## Check Tests Source Codes

There is an extensive set of test implemented in `ProfileServerProtocolTests` project that is part of the profile server solution.
Every feature that profile server offers through its interfaces has one or more tests implemented to verify the profile server implementation 
behaves as expected. You could learn from the source codes of the tests on how to correctly make requests to profile server. 
Each test is implemented according to its specification, which is linked from the test's source code, and which can be found in 
the IoP Message Protocol repository under [Profile Server Tests](https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#profile-server-tests).

Note that the implementation of the tests does not come in the production code quality. Especially, error handling is mostly missing 
in the code of the tests. So, you can get inspired on how to build your requests, but you should not reuse the code of the tests in your project 
if it is intended for production.


---
[Locating Service Port](CA-Locating-Service-Port.md) « [Index](CLIENT-APPS.md)
