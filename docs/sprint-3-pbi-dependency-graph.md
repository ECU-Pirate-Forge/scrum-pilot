# Scrum Pilot Sprint 3 PBI Dependency Graph

Historical sprint record with retired integration work omitted. Current data
storage and deployment are described in [architecture](architecture.md) and
[operations](multi-tenancy-operations.md).

```mermaid
graph TD

    PBI91["#91 Rename the GenerateAIStory List Overload Method"]
    PBI89["#89 Commit AI Generated Stories"]
    PBI74["#74 Commit Individual AI Generated Stories"]
    PBI90["#90 Display Generated Stories in a Pop-Up Modal"]
    PBI92["#92 Add bUnit Front-End Component Tests"]

    PBI93["#93 Update Entity Framework to Version 10"]
    PBI95["#95 Create enum for Origin and assign accordingly"]
    PBI96["#96 Convert Story Points to a Fibonacci Enum"]

    PBI75["#75 Update GitHub Actions Workflow"]
    PBI94["#94 Update Story Status When Dragging Cards on the Scrum Board"]

    PBI119["#119 Connect Frontend to Backend"]

    PBI90 --> PBI89
```