using DrivingSchool.Api;

var app = SchoolHost.Build(args);
await SchoolHost.Initialize(app);
await app.RunAsync();

public partial class Program;
