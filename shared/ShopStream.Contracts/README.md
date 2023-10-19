# ShopStream.Contracts

Plain records shared by the .NET services: event payloads (`Events.cs`), the event envelope, topic
names (`Topics.cs`), Kafka header names, roles and HTTP header names. No dependencies.

The Python and Node services mirror these shapes by hand - the JSON is camelCase and every envelope
carries `schemaVersion`. When a payload changes in a non-additive way, bump the version and keep
consumers able to read both for a release.
