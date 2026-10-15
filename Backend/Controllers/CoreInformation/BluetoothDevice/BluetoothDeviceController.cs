using Backend.Models.CoreInformation.BluetoothDevice;
using Backend.Repositories.CoreInformation.BluetoothDevice;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers.CoreInformation.BluetoothDevice;

[ApiController]
[Tags("Core Information")]
[Route("api/core-information/bluetooth-device")]
public sealed class BluetoothDeviceController(BluetoothDeviceRepository repository) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BluetoothDeviceResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BluetoothDeviceResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await repository.GetConnectedDeviceAsync(cancellationToken));
}
