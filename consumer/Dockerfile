FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 as execute
COPY out/ app/
ENTRYPOINT ["dotnet", "app/consumer.dll"]
