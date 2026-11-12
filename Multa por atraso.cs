@page "/multas"
@using System.ComponentModel.DataAnnotations
@using Microsoft.EntityFrameworkCore
@using AlquilerVehiculos.Modelos
@using AlquilerVehiculos.Datos

@inject IDbContextFactory<AlquilerDbContext> DbFactory

<PageTitle>Gestión de Multas por Atraso</PageTitle>

<h1>Gestión de Multas por Atraso</h1>
<p>Registro y consulta de multas asociadas a los contratos.</p>

@if (!string.IsNullOrWhiteSpace(error))
{
    <div class="alert alert-danger">@error</div>
}

@if (!string.IsNullOrWhiteSpace(mensaje))
{
    <div class="alert alert-success">@mensaje</div>
}

@if (cargando)
{
    <p>Cargando información...</p>
}
else
{
    <div class="card mb-4">
        <div class="card-body">
            <h5>Registrar Multa por Atraso</h5>

            @if (contratos.Count == 0 || usuarios.Count == 0)
            {
                <div class="alert alert-warning">
                    Se necesita al menos un contrato y un usuario activo para registrar multas.
                </div>
            }
            else
            {
                <EditForm Model="formulario" OnValidSubmit="GuardarMulta">
                    <DataAnnotationsValidator />
                    <ValidationSummary />

                    <div class="row">
                        <div class="col-md-6 mb-3">
                            <label class="form-label">Contrato</label>
                            <InputSelect class="form-select" @bind-Value="formulario.IdContrato">
                                <option value="0">Seleccione un contrato</option>
                                @foreach (var contrato in contratos)
                                {
                                    <option value="@contrato.IdContrato">
                                        Contrato #@contrato.IdContrato - @contrato.IdClienteNavigation.Nombre @contrato.IdClienteNavigation.Apellido
                                    </option>
                                }
                            </InputSelect>
                            <ValidationMessage For="() => formulario.IdContrato" />
                        </div>

                        <div class="col-md-6 mb-3">
                            <label class="form-label">Usuario que registra</label>
                            <InputSelect class="form-select" @bind-Value="formulario.IdUsuario">
                                <option value="0">Seleccione un usuario</option>
                                @foreach (var usuario in usuarios)
                                {
                                    <option value="@usuario.IdUsuario">@usuario.NombreCompleto</option>
                                }
                            </InputSelect>
                            <ValidationMessage For="() => formulario.IdUsuario" />
                        </div>

                        <div class="col-md-6 mb-3">
                            <label class="form-label">Días de retraso</label>
                            <InputNumber class="form-control" @bind-Value="formulario.DiasRetraso" min="1" />
                            <ValidationMessage For="() => formulario.DiasRetraso" />
                        </div>

                        <div class="col-md-6 mb-3">
                            <label class="form-label">Costo por día</label>
                            <InputNumber class="form-control" @bind-Value="formulario.CostoPorDia" min="0" step="0.01" />
                            <ValidationMessage For="() => formulario.CostoPorDia" />
                        </div>
                    </div>

                    <button type="submit" class="btn btn-primary" disabled="@guardando">
                        @if (guardando)
                        {
                            <span>Guardando...</span>
                        }
                        else
                        {
                            <span>Registrar Multa</span>
                        }
                    </button>
                </EditForm>
            }
        </div>
    </div>

    <div class="card mb-4">
        <div class="card-body">
            <h5>Datos disponibles</h5>
            <p>Contratos: @contratos.Count</p>
            <p>Usuarios activos: @usuarios.Count</p>
            <p>Multas registradas: @multas.Count</p>
        </div>
    </div>

    <h5>Multas registradas</h5>

    @if (multas.Count == 0)
    {
        <div class="alert alert-info">
            Hoy no hay multas registradas.
        </div>
    }
    else
    {
        <div class="table-responsive">
            <table class="table table-striped table-hover">
                <thead>
                    <tr>
                        <th>ID Multa</th>
                        <th>N.º Contrato</th>
                        <th>Cliente</th>
                        <th>Días de Retraso</th>
                        <th>Costo Total</th>
                        <th>Usuario</th>
                        <th>Fecha</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var multa in multas)
                    {
                        <tr @key="multa.IdMulta">
                            <td>@multa.IdMulta</td>
                            <td>#@multa.IdContrato</td>
                            <td>
                                @multa.IdContratoNavigation.IdClienteNavigation.Nombre
                                @multa.IdContratoNavigation.IdClienteNavigation.Apellido
                            </td>
                            <td>@multa.DiasRetraso</td>
                            <td>@multa.CostoTotal.ToString("N2")</td>
                            <td>@multa.IdUsuarioNavigation.NombreCompleto</td>
                            <td>@multa.FechaRegistro.ToString("dd/MM/yyyy")</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    }
}

@code {
    private List<Multa> multas = new();
    private List<Contrato> contratos = new();
    private List<Usuario> usuarios = new();

    private FormularioMulta formulario = new();

    private bool cargando = true;
    private bool guardando;
    private string? error;
    private string? mensaje;

    protected override async Task OnInitializedAsync()
    {
        await CargarDatos();
    }

    private async Task CargarDatos()
    {
        cargando = true;
        error = null;

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            contratos = await db.Contrato
                .AsNoTracking()
                .Include(c => c.IdClienteNavigation)
                .OrderByDescending(c => c.IdContrato)
                .ToListAsync();

            usuarios = await db.Usuario
                .AsNoTracking()
                .Where(u => u.Activo)
                .OrderBy(u => u.NombreCompleto)
                .ToListAsync();

            multas = await db.Multa
                .AsNoTracking()
                .Include(m => m.IdContratoNavigation)
                    .ThenInclude(c => c.IdClienteNavigation)
                .Include(m => m.IdUsuarioNavigation)
                .OrderByDescending(m => m.FechaRegistro)
                .ToListAsync();
        }
        catch (Exception)
        {
            error = "No se pudo cargar los datos. Verifique la conexión con SQL Server.";
        }
        finally
        {
            cargando = false;
        }
    }

    private async Task GuardarMulta()
    {
        error = null;
        mensaje = null;

        if (formulario.IdContrato <= 0 || formulario.IdUsuario <= 0)
        {
            error = "Debes seleccionar un contrato y un usuario.";
            return;
        }

        if (formulario.DiasRetraso < 1)
        {
            error = "Los días de retraso deben ser al menos 1.";
            return;
        }

        if (formulario.CostoPorDia < 0)
        {
            error = "El costo por día no puede ser negativo.";
            return;
        }

        guardando = true;

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            var contratoExiste = await db.Contrato
                .AnyAsync(c => c.IdContrato == formulario.IdContrato);

            var usuarioExiste = await db.Usuario
                .AnyAsync(u => u.IdUsuario == formulario.IdUsuario && u.Activo);

            if (!contratoExiste || !usuarioExiste)
            {
                error = "El contrato o el usuario seleccionado no está disponible.";
                return;
            }

            var nuevaMulta = new Multa
            {
                IdContrato = formulario.IdContrato,
                IdUsuario = formulario.IdUsuario,
                DiasRetraso = formulario.DiasRetraso,
                CostoTotal = formulario.DiasRetraso * formulario.CostoPorDia,
                FechaRegistro = DateTime.Now
            };

            db.Multa.Add(nuevaMulta);
            await db.SaveChangesAsync();

            mensaje = "Multa registrada correctamente.";

            formulario = new FormularioMulta();

            await CargarDatos();
        }
        catch (Exception)
        {
            error = "No se pudo registrar la multa. Verifica los datos e intenta nuevamente.";
        }
        finally
        {
            guardando = false;
        }
    }

    private class FormularioMulta
    {
        [Range(1, int.MaxValue, ErrorMessage = "Selecciona un contrato.")]
        public int IdContrato { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Selecciona un usuario.")]
        public int IdUsuario { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Los días de retraso deben ser al menos 1.")]
        public int DiasRetraso { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "El costo por día no puede ser negativo.")]
        public decimal CostoPorDia { get; set; }
    }
}
