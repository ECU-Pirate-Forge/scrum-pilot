Generate Initial User Stories from Problem Statement #4



Open 2 Linux Terminals and run API and UI on two ports (Open 2 Terminals in VS)

Terminal A: API

cd ~/scrum-pilot/UI_InputPS.Api
dotnet run (Click X on Open Browser Dialog)

Note the URL it prints (example: http://localhost:5001) Needs to match URL in generate.razor file in UI_InputPS.web directory).

Terminal B: UI

cd ~/scrum-pilot/UI_InputPS.Web
dotnet run 

Demo

•	Input the following Problem Statement

Our Scrum team is struggling with inconsistent sprint velocity and unclear acceptance criteria. We need a structured way to improve backlog refinement and ensure user stories are properly defined before sprint planning.

•	Verify structured API response 
•	Verify readable
•	Verify error checking on input
•	Verify persisted with JSON
		cat ~/scrum-pilot/UI_InputPS.Api/data/requests.json
