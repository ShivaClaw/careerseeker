<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Scout/SeekerSvc.Scout.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- Sqlite backend needs the Microsoft.Data.Sqlite package; excluded from the offline sandbox build.
         The in-memory ISeekerStore covers tests and the vertical slice. Re-included in the real tree. -->
    <Compile Remove="SqliteSeekerStore.cs" />
  </ItemGroup>
</Project>
