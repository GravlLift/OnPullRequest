# On Pull Request

A small Azure function that, when it receives a PR updated to Closed state webhook from Azure Devops, will create a realease using the latest successful build for that PR.

## Setup

After you have deployed OnPullRequest.csproj to an Azure Function, each devops project that you want to handle must have three configuration values set for it:

- `<ProjectName>:PersonalAccessToken` - The personal access token to be used to access devops. The PAT needs the `Build (Read)` and `Release (Read, Write, & Execute)` permissions. Further information on creating a PAT can be found [here](https://docs.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops&tabs=preview-page#create-a-pat).
- `<ProjectName>:DevopsBaseUrl` - The base url for your DevOps environment, usually in the form of `https://dev.azure.com/Organization/`.
- `<ProjectName>:ReleaseDefinitionId` - The identifier of the release definition you want to trigger. This can be found in the URL of your releases page, proceed by `definitionId=`.

So for https://dev.azure.com/fabrikamorganization/fabrikamproject, you might see the following configuration values:

```
"fabrikamproject:PersonalAccessToken": "zumbevdob60j8xtiw3t4np4z0rmmg00b5efdezr2rjyqnm94uxx6",
"fabrikamproject:DevopsBaseUrl": "https://dev.azure.com/fabrikamorganization/",
"fabrikamproject:ReleaseDefinitionId": 3
```

Finally, on your DevOps instance, [create a Webhook](https://docs.microsoft.com/en-us/azure/devops/service-hooks/services/webhooks?view=azure-devops) to trigger on the "Pull request updated" event, targeting your Azure Function. `All` must be selected for `Resource details to send`. You may also wish to consider including a [function key](https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-http-webhook-trigger?tabs=csharp#authorization-keys) to prevent malicious users from running your function.
