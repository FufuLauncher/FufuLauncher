/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
namespace FufuLauncher.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}

