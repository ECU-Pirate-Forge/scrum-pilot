# Scrum Pilot Sprint 3 PBI Dependency Graph

```mermaid
graph TD

    PBI97["#97 List Required API Endpoints for Discord Bot Integration"]
    PBI101["#101 Implement Required API Endpoints for Discord Bot Integration"]

    PBI91["#91 Rename the GenerateAIStory List Overload Method"]
    PBI89["#89 Commit AI Generated Stories"]
    PBI74["#74 Commit Individual AI Generated Stories"]
    PBI90["#90 Display Generated Stories in a Pop-Up Modal"]
    PBI92["#92 Add bUnit Front-End Component Tests"]

    PBI93["#93 Update Entity Framework to Version 10"]
    PBI95["#95 Create enum for Origin and assign accordingly"]
    PBI96["#96 Convert Story Points to a Fibonacci Enum"]

    PBI100["#100 Research Whether Audio Files Can Be Stored in SQLite"]
    PBI99["#99 Fix File Size Bug in Transcription Service"]
    PBI98["#98 Implement Proper Craig Integration With Separate Voice Recordings"]

    PBI75["#75 Update GitHub Actions Workflow"]
    PBI94["#94 Update Story Status When Dragging Cards on the Scrum Board"]

    PBI97 --> PBI101