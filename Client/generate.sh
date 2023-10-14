VERSION=0.1.1

docker run --rm -v "${PWD}:/local" --network host -u $(id -u ${USER}):$(id -g ${USER})  openapitools/openapi-generator-cli generate \
-i http://localhost:5021/swagger/v1/swagger.json \
-g csharp \
-o /local/out --additional-properties=packageName=Coflnet.Sky.McConnect,packageVersion=$VERSION,licenseId=MIT

cd out
sed -i 's/GIT_USER_ID/Coflnet/g' src/Coflnet.Sky.McConnect/Coflnet.Sky.McConnect.csproj
sed -i 's/GIT_REPO_ID/SkyMcConnect/g' src/Coflnet.Sky.McConnect/Coflnet.Sky.McConnect.csproj
sed -i 's/>OpenAPI/>Coflnet/g' src/Coflnet.Sky.McConnect/Coflnet.Sky.McConnect.csproj


dotnet pack
cp src/Coflnet.Sky.McConnect/bin/Debug/Coflnet.Sky.McConnect.*.nupkg ..
