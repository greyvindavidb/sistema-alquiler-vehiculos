
using System;
using System.Globalization;
using System.Threading.Tasks;
using AlquilerVehiculos.Data;
using AlquilerVehiculos.Models;
using Microsoft.EntityFrameworkCore;

namespace AlquilerVehiculos.Services;

public class MultaService
{
    public async Task<decimal> RegistrarMultaAsync(
        AlquilerDbContext db,
        Contrato contrato,
        DateTime fechaDevolucion)
    {
        var parametros = await db.ParametroSistema
            .Where(p =>
                p.Clave == "TOLERANCIA_RETRASO_MINUTOS" ||
                p.Clave == "PORCENTAJE_MULTA_DIA")
            .ToListAsync();

        var toleranciaTexto = parametros
            .FirstOrDefault(p => p.Clave == "TOLERANCIA_RETRASO_MINUTOS")?.Valor;

        var porcentajeTexto = parametros
            .FirstOrDefault(p => p.Clave == "PORCENTAJE_MULTA_DIA")?.Valor;

        if (!int.TryParse(toleranciaTexto, out int tolerancia) ||
            !decimal.TryParse(porcentajeTexto, NumberStyles.Number,
                CultureInfo.InvariantCulture, out decimal porcentaje))
        {
            throw new InvalidOperationException(
                "Los parámetros de multa no están configurados correctamente.");
        }

        if (tolerancia < 0 || porcentaje < 0)
        {
            throw new InvalidOperationException(
                "Los parámetros de multa no pueden ser negativos.");
        }

        double minutosRetraso =
            (fechaDevolucion - contrato.FechaFinPactada).TotalMinutes;

        if (minutosRetraso <= tolerancia)
            return 0m;

        decimal diasMulta = Math.Ceiling(
            (decimal)minutosRetraso / 1440m);

        decimal monto = Math.Round(
            diasMulta * contrato.PrecioDiaAplicado * porcentaje / 100m,
            2);

        var tipoRetraso = await db.TipoCargo
            .FirstOrDefaultAsync(t =>
                t.Nombre.ToLower() == "retraso" && t.Activo);

        if (tipoRetraso == null)
        {
            throw new InvalidOperationException(
                "No existe un tipo de cargo Retraso activo.");
        }

        db.Cargo.Add(new Cargo
        {
            IdContrato = contrato.IdContrato,
            IdTipoCargo = tipoRetraso.IdTipoCargo,
            Descripcion = $"Multa por retraso en la devolución. " +
                          $"Tiempo de retraso: {Math.Ceiling(minutosRetraso)} minutos.",
            Monto = monto,
            Fecha = fechaDevolucion
        });

        return monto;
    }
}
